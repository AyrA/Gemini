using Gemini.Lib;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Gemini.Server
{
    public class GeminiHostScanner
    {
        private static readonly List<GeminiHost> _hosts = new();
        private static readonly ILogger logger = Tools.GetLogger<GeminiHostScanner>();
        public static GeminiHost[] Hosts => _hosts.ToArray();

        static GeminiHostScanner()
        {
            LoadHosts(Assembly.GetExecutingAssembly());
            var pluginDir = Path.Combine(AppContext.BaseDirectory, "Hosts");
            if (!Directory.Exists(pluginDir))
            {
                logger.LogInformation("Directory {path} does not exist. Stop plugin loading", pluginDir);
                return;
            }
            foreach (var plugin in Directory.GetFiles(pluginDir, "*.dll"))
            {
                try
                {
                    var a = Assembly.LoadFile(plugin);
                    LoadHosts(a);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load {plugin} as assembly", plugin);
                }
            }
        }

        static void LoadHosts(Assembly a)
        {
            foreach (var t in a.GetTypes())
            {
                if (t.IsSubclassOf(typeof(GeminiHost)))
                {
                    logger.LogInformation("Found {host} as {interface} implementation", t.FullName, nameof(GeminiHost));
                    var constructor = t.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                    {
                        GeminiHost host;
                        try
                        {
                            host = (GeminiHost)constructor.Invoke(Array.Empty<object>());
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Constructor of {plugin} failed", t.FullName);
                            continue;
                        }
                        try
                        {
                            host.Start();
                            logger.LogInformation("Started {plugin}", t.FullName);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, ".Start() of {plugin} failed", t.FullName);
                            continue;
                        }
                        logger.LogInformation("Registered Gemini host: {plugin}", t.FullName);
                        _hosts.Add(host);
                    }
                    else
                    {
                        logger.LogWarning("Type {plugin} is a gemini host but lacks a parameterless constructor", t.FullName);
                    }
                }
            }
        }
    }
}
