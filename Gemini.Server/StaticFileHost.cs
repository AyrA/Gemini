using Gemini.Lib;
using System.Net;
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
        public override async Task<GeminiResponse?> Request(Uri url, EndPoint clientAddress)
        {
            var p = Path.GetFullPath(Path.Combine(_root, url.LocalPath[1..]));
            if (p != _root && !p.StartsWith(_root + Path.DirectorySeparatorChar))
            {
                Console.WriteLine("ERR: Path mapped to {0}, which is outside of {1}", p, _root);
                return await Task.FromResult(GeminiResponse.BadRequest());
            }
            if (Directory.Exists(p))
            {
                if (_dirBrowse)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"# Directory Listing of {url.LocalPath}");
                    foreach (var item in Directory.EnumerateFileSystemEntries(p))
                    {
                        var link = string.Join("/", item[p.Length..]
                            .Split(Path.DirectorySeparatorChar)
                            .Select(m => Uri.EscapeDataString(m)));
                        var entry = item[(p.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
                        sb.AppendLine($"=> {link} {entry}");
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
            return null;
        }

        public override bool IsAccepted(Uri url, IPAddress remoteAddress) => true;
    }
}
