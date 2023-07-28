using Gemini.Lib;
using Gemini.Lib.Network;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Server.Network
{
    public class StaticFileHost : GeminiHost
    {
        private class StaticFileHostConfig
        {
            private string[] _remoteRanges = Array.Empty<string>();
            private string[] _hosts = Array.Empty<string>();
            private string[] _thumbprints = Array.Empty<string>();

            public string RootDirectory { get; set; } = string.Empty;
            public bool AllowDirectoryBrowsing { get; set; }

            public string[] Hosts
            {
                get
                {
                    return _hosts;
                }

                set
                {
                    _hosts = value.Select(Normalize).ToArray();
                    isGlobal = _hosts.Contains("*:*");
                }
            }

            public string[] RemoteRanges
            {
                get
                {
                    return _remoteRanges;
                }
                set
                {
                    ipRanges = value.Select(m => IpRange.Parse(m)).ToArray();
                    _remoteRanges = value;
                }
            }

            public string[] Thumbprints
            {
                get
                {
                    return _thumbprints;
                }
                set
                {
                    if (value.Length > 0 && !value.All(Certificates.IsValidThumbprint))
                    {
                        throw new ArgumentException("Invalid thumbprints in array");
                    }
                    _thumbprints = value.Select(m => m.ToUpper()).ToArray();
                }
            }

            private bool isGlobal;
            private IpRange[] ipRanges = Array.Empty<IpRange>();

            public void Validate()
            {
                if (string.IsNullOrWhiteSpace(RootDirectory))
                {
                    throw new Exception("Root directory cannot be null, empty or whitespace");
                }
                if (!Directory.Exists(RootDirectory))
                {
                    throw new Exception($"Root directory '{RootDirectory}' does not exist");
                }
                if (_hosts == null || _hosts.Length == 0)
                {
                    throw new Exception("Host list cannot be null or empty. Use an asterisk to create a global host");
                }
                if (!_hosts.All(IsValidHostSpec))
                {
                    throw new Exception("At least one host spec is invalid");
                }
            }

            public bool IsMatch(string host)
            {
                if (Hosts == null || Hosts.Length == 0)
                {
                    throw new InvalidOperationException("Host list is null or empty");
                }
                if (isGlobal)
                {
                    return true;
                }
                if (!IsValidHostSpec(host))
                {
                    logger.LogError("Invalid host spec: {host}", host);
                    throw new ArgumentException($"'{nameof(host)}' cannot be null or whitespace.", nameof(host));
                }
                //Normalize the host name and then check
                host = Normalize(host);
                return Hosts.Any(m => m == host);
            }

            public static bool IsValidHostSpec(string host)
            {
                if (string.IsNullOrWhiteSpace(host))
                {
                    return false;
                }
                if (host == "*" || host == "*:*")
                {
                    return true;
                }
                //Plain host name, IPv4 or IPv6 address
                if (Uri.CheckHostName(host) != UriHostNameType.Unknown)
                {
                    return true;
                }
                //IPv6 match
                var match = Regex.Match(host, @"^\[(.+)\]:(\d+|\*)$");
                if (!match.Success)
                {
                    //Failed. Generic match
                    match = Regex.Match(host, @"^(.+):(\d+|\*)$");
                }
                //Invalid
                if (!match.Success)
                {
                    return false;
                }
                var hostPortion = match.Groups[1].Value;
                var portPortion = match.Groups[2].Value;

                //Check port
                if (portPortion != "*" && !ushort.TryParse(portPortion, out _))
                {
                    return false;
                }
                //Check host
                return Uri.CheckHostName(hostPortion) != UriHostNameType.Unknown;
            }

            public bool IsAcceptedRemoteAddress(IPAddress address)
            {
                logger.LogDebug("Checking {ip} for access rights", address);
                if (ipRanges == null || ipRanges.Length == 0)
                {
                    return true;
                }
                return ipRanges.Any(m => m.InRange(address));
            }

            public bool IsAcceptedCertificate(X509Certificate? cert) => IsAcceptedCertificate((X509Certificate2?)cert);

            public bool IsAcceptedCertificate(X509Certificate2? cert)
            {
                if (cert == null)
                {
                    return _thumbprints?.Length == 0;
                }
                return _thumbprints.Contains(cert.Thumbprint.ToUpper());
            }

            /// <summary>
            /// Sets the port to "*" if no port was specified
            /// Adds brackets to IPv6 host names
            /// </summary>
            /// <param name="host">host spec</param>
            /// <returns>Normalized host spec</returns>
            /// <remarks>Invalid names are returned as-is</remarks>
            private static string Normalize(string host)
            {
                if (string.IsNullOrWhiteSpace(host))
                {
                    logger.LogWarning("Invalid host spec. IsNullOrWhiteSpace");
                    return host;
                }
                if (host == "*" || host == "*:*")
                {
                    return "*:*";
                }
                if (Regex.IsMatch(host, @"^\*:\d+$"))
                {
                    if (!ushort.TryParse(host.Split(':').Last(), out _))
                    {
                        logger.LogWarning("Invalid port of host spec {host}. Neither valid TCP port nor a sole asterisk", host);
                    }
                    return host;
                }

                switch (Uri.CheckHostName(host))
                {
                    case UriHostNameType.Basic:
                    case UriHostNameType.Dns:
                        logger.LogDebug("Parsed dns host");
                        return host.ToLower() + ":*";
                    case UriHostNameType.IPv4:
                        logger.LogDebug("Parsed IPv4 address as host");
                        return $"{IPAddress.Parse(host)}:*";
                    case UriHostNameType.IPv6:
                        logger.LogDebug("Parsed IPv6 address as host");
                        return $"[{IPAddress.Parse(host)}]:*";
                }

                //At this point the host is invalid or has a port spec

                //IPv6 match
                var match = Regex.Match(host, @"^\[(.+)\]:(\d+|\*)$");
                if (!match.Success)
                {
                    //Failed. Generic match
                    match = Regex.Match(host, @"^(.+):(\d+|\*)$");
                }
                //Invalid host. Return as-is
                if (!match.Success)
                {
                    logger.LogWarning("Invalid host spec: {host}", host);
                    return host;
                }
                //Check port
                var port = match.Groups[2].Value;
                if (port != "*" && !ushort.TryParse(port, out _))
                {
                    logger.LogWarning("Invalid port of host spec {host}. Neither valid TCP port nor a sole asterisk", host);
                    return host;
                }

                return Normalize(host) + $":{port}";
            }
        }

        private readonly StaticFileHostConfig[] _config;

        private static readonly ILogger logger = Tools.GetLogger<StaticFileHost>();

        public StaticFileHost()
        {
            var jsonFile = Path.Combine(AppContext.BaseDirectory, $"{nameof(StaticFileHost)}.json");
            if (File.Exists(jsonFile))
            {
                try
                {
                    _config = File.ReadAllText(jsonFile).FromJson<StaticFileHostConfig[]>()
                        ?? throw new Exception("Deserialized configuration is null");
                    if (_config.Length == 0)
                    {
                        throw new Exception("Deserialized configuration is empty");
                    }
                    foreach (var c in _config)
                    {
                        logger.LogDebug("Validating Configuration");
                        c.Validate();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unable to deserialize configuration");
                    throw;
                }
            }
            else
            {
                logger.LogWarning("Cannot find configuration: {file}. Creating a default listener instead", jsonFile);
                _config = new StaticFileHostConfig[]
                {
                    new StaticFileHostConfig()
                    {
                        RootDirectory = Path.Combine(AppContext.BaseDirectory, "StaticFileHost"),
                        Hosts = new string[]{ "*" },
                        AllowDirectoryBrowsing = false
                    }
                };
                _config[0].Validate();
            }
        }

        public StaticFileHost(string rootDir, bool enableDirectoryBrowsing, string host = "*")
        {
            _config = new StaticFileHostConfig[]
            {
                new StaticFileHostConfig()
                {
                    RootDirectory = Path.GetFullPath(rootDir),
                    Hosts = new string[]{ host },
                    AllowDirectoryBrowsing = enableDirectoryBrowsing
                }
            };
            _config[0].Validate();
        }

        public override async Task<GeminiResponse?> Request(Uri url, IPEndPoint client, X509Certificate? cert)
        {
            var host = GetHostConfig(url)
                ?? throw new InvalidOperationException(
                    $"This {nameof(StaticFileHost)} does not accept requests for {url.Host}");

            var _root = host.RootDirectory;
            var _dirBrowse = host.AllowDirectoryBrowsing;

            if (!host.IsAcceptedCertificate(cert))
            {
                logger.LogWarning("Certificate was not accepted");
                return await Task.FromResult(GeminiResponse.CertificateRequired());
            }

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

        public override bool IsAccepted(Uri _1, IPAddress _2, X509Certificate? _3)
        {
            var host = GetHostConfig(_1);
            if (host == null)
            {
                return false;
            }
            return host.IsAcceptedRemoteAddress(_2);
            //Certificate check is not done here to facilitate status code 60
        }

        private StaticFileHostConfig? GetHostConfig(Uri url) => GetHostConfig(url.Host);

        private StaticFileHostConfig? GetHostConfig(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException($"'{nameof(host)}' cannot be null or whitespace.", nameof(host));
            }
            logger.LogDebug("Getting config for {host}", host);
            return _config.FirstOrDefault(m => m.IsMatch(host));
        }

#if DEBUG
        public void RegisterThumbprint(string thumbprint)
        {
            logger.LogDebug("Registering {thumb} with host instance", thumbprint);
            foreach (var config in _config)
            {
                config.Thumbprints = config.Thumbprints.Concat(new[] { thumbprint }).ToArray();
            }
        }
#endif
    }
}
