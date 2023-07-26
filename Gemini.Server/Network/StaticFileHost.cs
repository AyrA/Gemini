using Gemini.Lib;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Gemini.Server.Network
{
    public class StaticFileHost : GeminiHost
    {
        private class StaticFileHostConfig
        {
            public string? RootDirectory { get; set; }
            public bool AllowDirectoryBrowsing { get; set; }
        }

        private readonly string _root;
        private readonly bool _dirBrowse;

        private static readonly ILogger logger = Tools.GetLogger<StaticFileHost>();

        public StaticFileHost()
        {
            var jsonFile = Path.Combine(AppContext.BaseDirectory, $"{nameof(StaticFileHost)}.json");
            if (File.Exists(jsonFile))
            {
                try
                {
                    var config = File.ReadAllText(jsonFile).FromJson<StaticFileHostConfig>()
                        ?? throw new Exception("Deserialized configuration is null");
                    _root = config.RootDirectory ?? throw new Exception("Directory of deserialized config is null");
                    _dirBrowse = config.AllowDirectoryBrowsing;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unable to deserialize configuration");
                    throw;
                }
            }
            else
            {
                logger.LogWarning("Cannot find configuration: {file}", jsonFile);
                _root = Path.Combine(AppContext.BaseDirectory, "StaticFileHost");
                _dirBrowse = false;
            }
        }

        public StaticFileHost(string rootDir, bool enableDirectoryBrowsing)
        {
            _root = Path.GetFullPath(rootDir);
            _dirBrowse = enableDirectoryBrowsing;
        }

        public override async Task<GeminiResponse?> Request(Uri url, IPEndPoint client, X509Certificate? ignored)
        {
            var p = Path.GetFullPath(Path.Combine(_root, url.LocalPath[1..])).TrimEnd(Path.DirectorySeparatorChar);
            if (p != _root && !p.StartsWith(_root + Path.DirectorySeparatorChar))
            {
                logger.LogWarning("Possible path traversal attack by {ip}: Path mapped to {path}, which is outside of {root}", client, p, _root);
                return await Task.FromResult(GeminiResponse.BadRequest());
            }
            if (Directory.Exists(p))
            {
                if (_dirBrowse)
                {
                    var di = new DirectoryInfo(p);
                    //Add trailing slash for directory URLs
                    if (!url.LocalPath.EndsWith("/"))
                    {
                        return GeminiResponse.Redirect(url.LocalPath + "/");
                    }

                    logger.LogInformation("Building directory listing of {path} for {client}", p, client);

                    var sb = new StringBuilder();
                    sb.AppendLine($"# Directory Listing of {url.LocalPath}");

                    if (di.FullName != _root)
                    {
                        sb.AppendLine("=> ../ [UP]");
                    }

                    //Directories first, then files
                    foreach (var dir in di.EnumerateDirectories())
                    {
                        var link = Uri.EscapeDataString(dir.Name);
                        sb.AppendLine($"=> {link}/ \uD83D\uDCC1 {dir.Name}");
                    }
                    foreach (var file in di.EnumerateFiles())
                    {
                        var link = Uri.EscapeDataString(file.Name);
                        sb.AppendLine($"=> {link} \uD83D\uDCC4 {file.Name}");
                    }
                    sb.AppendLine($"Generated for {client} at {DateTime.UtcNow}");
                    return await Task.FromResult(GeminiResponse.Ok(sb.ToString()));
                }
                return await Task.FromResult(new GeminiResponse(StatusCode.TemporaryFailure, null, "Forbidden"));
            }
            if (File.Exists(p))
            {
                logger.LogInformation("Sending file {file} to {client}", p, client);
                return await Task.FromResult(GeminiResponse.File(p));
            }
            return await Task.FromResult(GeminiResponse.NotFound());
        }

        public override bool IsAccepted(Uri _1, IPAddress _2, X509Certificate? _3) => true;
    }
}
