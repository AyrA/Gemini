using AyrA.AutoDI;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Gemini.Server
{
    public static class GeminiHostInstaller
    {
        private static bool loaded = false;
        private static readonly string pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins");

        private class InfoJson
        {
            public Guid? Id { get; set; }

            public Version? Version { get; set; }

            public string[]? Preserve { get; set; }

            [MemberNotNull(nameof(Id), nameof(Version))]
            public void Validate()
            {
                if (Id == null || Id.Value == Guid.Empty)
                {
                    throw new Exception($"Id cannot be null or {Guid.Empty}");
                }
                if (Version == null)
                {
                    throw new Exception("Version is not set in info.json");
                }
            }
        }

        public static void Install(string pathToZip)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pathToZip);

            using var fs = File.OpenRead(pathToZip);
            using var arc = new ZipArchive(fs, ZipArchiveMode.Read);
            var infoEntry = arc.Entries.FirstOrDefault(m => m.FullName.Equals("info.json", StringComparison.InvariantCultureIgnoreCase))
                ?? throw new IOException("Unable to find info.json in the root of the zip archive");
            var pluginEntry = arc.Entries.FirstOrDefault(m => m.FullName.Equals("plugin.dll", StringComparison.InvariantCultureIgnoreCase))
                ?? throw new IOException("Unable to find plugin.dll in the root of the zip archive");
            using var entryStream = infoEntry.Open();
            using var msInfo = new MemoryStream();
            entryStream.CopyTo(msInfo);
            var zipPluginInfo = Encoding.UTF8.GetString(msInfo.ToArray()).FromJson<InfoJson>()
                ?? throw new InvalidDataException("Failed to deserialize info.json into a valid object");
            zipPluginInfo.Validate();

            var pluginName = zipPluginInfo.Id.Value.ToString();

            var installDir = Path.Combine(pluginDir, pluginName);
            if (Directory.Exists(installDir))
            {
                Console.WriteLine("Plugin exists, checking if zip is newer...");
                //Check if current plugin is newer. If it isn't. Refuse to install
                InfoJson existingInfo;
                try
                {
                    existingInfo = File.ReadAllText(Path.Combine(installDir, "info.json")).FromJson<InfoJson>()
                        ?? throw new Exception("Unable to read info.json from the existing plugin");
                    existingInfo.Validate();
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to parse info.json of plugin '{pluginName}'. See inner exception for details.", ex);
                }
                if (existingInfo.Version >= zipPluginInfo.Version)
                {
                    throw new Exception($"A plugin with the name '{pluginName}' already exists, but the installed version is not older than the new version");
                }
                if (existingInfo.Id != zipPluginInfo.Id)
                {
                    throw new Exception("Plugin already exists, but the ids in the info.json differ.");
                }
                Console.WriteLine("Zip file is newer than installed version. Deleting old version");
                //Plugin exists and is older. We can install over it.
                try
                {
                    Uninstall(zipPluginInfo.Id.Value, zipPluginInfo.Preserve ?? []);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Unable to uninstall existing plugin. Error: {ex.Message}", ex);
                }
            }
            Directory.CreateDirectory(installDir);
            foreach (var entry in arc.Entries)
            {
                var p = Path.GetFullPath(Path.Combine(installDir, entry.FullName));
                //Prevent path traversal attacks
                if (!p.StartsWith(installDir + Path.DirectorySeparatorChar))
                {
                    throw new Exception($"Possible path traversal attack. Name in zip archive was {entry.FullName}");
                }
                //If directory, create it
                if (entry.FullName.EndsWith('/'))
                {
                    Console.Error.WriteLine("Creating folder {0}...", entry.FullName);
                    Directory.CreateDirectory(p);
                    continue;
                }
                if (File.Exists(p))
                {
                    Console.Error.WriteLine($"File {0} already exists. Skipping it", entry.FullName);
                    continue;
                }
                Console.Error.WriteLine("Extracting {0}...", entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(p) ?? installDir);
                entry.ExtractToFile(p, false);
            }
            Console.WriteLine("Plugin has been installed");
        }

        public static void Uninstall(Guid pluginName) => Uninstall(pluginName, []);

        public static void Uninstall(Guid pluginName, string[] preserve)
        {
            if (pluginName == Guid.Empty)
            {
                throw new ArgumentException($"Plugin name cannot be {pluginName}");
            }
            var installDir = Path.Combine(pluginDir, pluginName.ToString());
            if (!Directory.Exists(installDir))
            {
                throw new Exception($"Unable to find plugin '{pluginName}'");
            }

            //Shortcut if nothing has to be preserved
            if (preserve.Length == 0)
            {
                Console.Error.WriteLine("Deleting {0} recursively because there are no entries to preserve", pluginName);
                Directory.Delete(installDir, true);
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                //Turn unix dir into windows dir and make case insensitive
                preserve = preserve
                    .Select(m => m.Replace('/', Path.DirectorySeparatorChar).ToLower())
                    .Distinct()
                    .ToArray();
            }
            else
            {
                //Turn windows dir into unix dir
                preserve = preserve
                    .Select(m => m.Replace('\\', Path.DirectorySeparatorChar))
                    .ToArray();
            }

            Console.Error.WriteLine("Uninstalling {0}. Have {1} entried to preserve", pluginName, preserve.Length);

            DeleteDir(installDir, installDir, preserve);
        }

        private static bool DeleteDir(string dir, string installDir, string[] preserve)
        {
            var keepDir = false;
            var matchDir = installDir == dir ? string.Empty : dir[(installDir.Length + 1)..];
            //Don't bother to scan files and subdirectories if this directory is to be preserved
            if (matchDir != string.Empty && preserve.Any(m => string.Compare(m, matchDir, StringComparison.OrdinalIgnoreCase) == 0))
            {
                Console.Error.WriteLine("Skipping to preserve: {0}", matchDir);
                return false;
            }
            //Delete all non-matching files
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var subEntry = file[(installDir.Length + 1)..];
                if (preserve.Any(m => string.Compare(m, subEntry, StringComparison.OrdinalIgnoreCase) == 0))
                {
                    Console.Error.WriteLine("Skipping to preserve: {0}", subEntry);
                    keepDir = true;
                }
                else
                {
                    Console.Error.WriteLine("Deleting {0}", subEntry);
                    DeleteFile(file);
                }
            }
            //Delete directories recursively
            foreach (var subdir in Directory.EnumerateDirectories(installDir))
            {
                if (!DeleteDir(subdir, installDir, preserve))
                {
                    keepDir = true;
                }
            }
            if (!keepDir)
            {
                Console.Error.WriteLine("Deleting: {0}", dir);
                DeleteDirectory(dir);
            }
            return !keepDir;
        }

        private static void DeleteFile(string file)
        {
            while (true)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to delete file {0}.", file);
                    Console.WriteLine("Error: {0}", ex.Message);
                    Console.WriteLine("Will retry in 2 seconds...");
                    Console.WriteLine("Fix the issue, or press CTRL+C to abort");
                    Thread.Sleep(2000);
                }
            }
        }

        private static void DeleteDirectory(string dir)
        {
            while (true)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to delete directory {0}.", dir);
                    Console.WriteLine("Error: {0}", ex.Message);
                    Console.WriteLine("Will retry in 2 seconds...");
                    Console.WriteLine("Fix the issue, or press CTRL+C to abort");
                    Thread.Sleep(2000);
                }
            }
        }

        public static void LoadPlugins(IServiceCollection services)
        {
            if (loaded)
            {
                return;
            }
            loaded = true;

            if (Directory.Exists(pluginDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(pluginDir))
                {
                    var pluginFile = Path.Combine(dir, "Plugin.dll");
                    if (File.Exists(pluginFile))
                    {
                        try
                        {
                            var a = Assembly.LoadFrom(pluginFile);
                            services.AutoRegisterFromAssembly(a);

                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("Failed to load plugin {0}\r\nReason: {1}", pluginFile, ex.Message);
                        }
                    }
                }
            }
        }
    }
}
