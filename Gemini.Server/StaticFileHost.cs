using Gemini.Lib;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Gemini.Server
{
    public class StaticFileHost : GeminiHost
    {
        private readonly string _root;
        private readonly bool _dirBrowse;

        public StaticFileHost(string rootDir, bool enableDirectoryBrowsing)
        {
            _root = Path.GetFullPath(rootDir);
            _dirBrowse = enableDirectoryBrowsing;
        }

        public override async Task<GeminiResponse?> Request(Uri url, IPEndPoint clientAddress, X509Certificate? ignored)
        {
            var p = Path.GetFullPath(Path.Combine(_root, url.LocalPath[1..])).TrimEnd(Path.DirectorySeparatorChar);
            if (p != _root && !p.StartsWith(_root + Path.DirectorySeparatorChar))
            {
                Console.WriteLine("ERR: Path mapped to {0}, which is outside of {1}", p, _root);
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
                    sb.AppendLine($"Generated for {clientAddress} at {DateTime.UtcNow}");
                    return await Task.FromResult(GeminiResponse.Ok(sb.ToString()));
                }
                return await Task.FromResult(new GeminiResponse(StatusCode.TemporaryFailure, null, "Forbidden"));
            }
            if (File.Exists(p))
            {
                if (!p.StartsWith(_root + Path.DirectorySeparatorChar))
                {
                    return await Task.FromResult(GeminiResponse.BadRequest());
                }
                return await Task.FromResult(GeminiResponse.File(p));
            }
            return await Task.FromResult(GeminiResponse.NotFound());
        }

        public override bool IsAccepted(Uri _1, IPAddress _2, X509Certificate? _3) => true;
    }
}
