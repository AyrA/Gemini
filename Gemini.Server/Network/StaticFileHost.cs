using AyrA.AutoDI;
using Gemini.Lib;
using Gemini.Lib.Data;
using Gemini.Lib.Network;
using Gemini.Lib.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Server.Network
{
    [AutoDIRegister(AutoDIType.Singleton)]
    public partial class StaticFileHost : GeminiHost
    {
        private class StaticFileHostJson
        {
            public bool Enabled { get; set; }
            public StaticFileHostConfig[]? Hosts { get; set; }
        }

        private partial class StaticFileHostConfig
        {
            private string[] _remoteRanges = [];
            private string[] _hosts = [];
            private string[] _thumbprints = [];

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
                    if (value.Length > 0 && !value.All(CertificateService.IsValidThumbprint))
                    {
                        throw new ArgumentException("Invalid thumbprints in array");
                    }
                    _thumbprints = value.Select(m => m.ToUpper()).ToArray();
                }
            }

            private bool isGlobal;
            private IpRange[] ipRanges = [];

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
                var match = GenericIpv6Endpoint().Match(host);
                if (!match.Success)
                {
                    //Failed. Generic match
                    match = GenericHostPortEndpoint().Match(host);
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
                    return host;
                }
                if (host == "*" || host == "*:*")
                {
                    return "*:*";
                }
                if (AnyHostMatcher().IsMatch(host))
                {
                    return host;
                }

                switch (Uri.CheckHostName(host))
                {
                    case UriHostNameType.Basic:
                    case UriHostNameType.Dns:
                        return host.ToLower() + ":*";
                    case UriHostNameType.IPv4:
                        return $"{IPAddress.Parse(host)}:*";
                    case UriHostNameType.IPv6:
                        return $"[{IPAddress.Parse(host)}]:*";
                }

                //At this point the host is invalid or has a port spec

                //IPv6 match
                var match = GenericIpv6Endpoint().Match(host);
                if (!match.Success)
                {
                    //Failed. Generic match
                    match = GenericHostPortEndpoint().Match(host);
                }
                //Invalid host. Return as-is
                if (!match.Success)
                {
                    return host;
                }
                //Check port
                var port = match.Groups[2].Value;
                if (port != "*" && !ushort.TryParse(port, out _))
                {
                    return host;
                }

                return Normalize(host) + $":{port}";
            }

            [GeneratedRegex(@"^\[(.+)\]:(\d+|\*)$")]
            private static partial Regex GenericIpv6Endpoint();
            [GeneratedRegex(@"^(.+):(\d+|\*)$")]
            private static partial Regex GenericHostPortEndpoint();
            [GeneratedRegex(@"^\*:\d+$")]
            private static partial Regex AnyHostMatcher();
        }

        private StaticFileHostJson? _config;
        private readonly ILogger<StaticFileHost> _logger;

        public StaticFileHost(ILogger<StaticFileHost> logger)
        {
            _logger = logger;
            Priority = ushort.MaxValue - 1;
        }

        public override bool Start()
        {
            _logger.LogInformation("Starting host and loading configuration");
            var jsonFile = Path.Combine(AppContext.BaseDirectory, $"{nameof(StaticFileHost)}.json");
            StaticFileHostJson? newConfig;
            if (File.Exists(jsonFile))
            {
                try
                {
                    newConfig = File.ReadAllText(jsonFile).FromJson<StaticFileHostJson>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to deserialize configuration. Stopping loading process");
                    return false;
                }

                if (newConfig == null)
                {
                    _logger.LogError("Deserialized configuration is null. Stopping loading process");
                    return false;
                }
                if (!newConfig.Enabled)
                {
                    _logger.LogInformation("Static file host is not enabled. Stopping loading process");
                    return false;
                }
                if (newConfig.Hosts == null || newConfig.Hosts.Length == 0)
                {
                    _logger.LogInformation("No hosts configured. Stopping loading process");
                    return false;
                }
                foreach (var c in newConfig.Hosts)
                {
                    if (c == null)
                    {
                        _logger.LogWarning("Host list contains null entry. Skipping for now");
                        continue;
                    }
                    _logger.LogDebug("Validating Configuration");
                    try
                    {
                        c.Validate();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to validate a static file host configuration value. Stopping loading process");
                        return false;
                    }
                }
            }
            else
            {
                var host = new StaticFileHostConfig()
                {
                    RootDirectory = Path.Combine(AppContext.BaseDirectory, "StaticFileHost"),
                    Hosts = ["*"],
                    AllowDirectoryBrowsing = false
                };
                _logger.LogWarning("Cannot find configuration: {file}. Creating a global listener for files in {path} instead", jsonFile, host.RootDirectory);
                Directory.CreateDirectory(host.RootDirectory);
                host.Validate();
                newConfig = new StaticFileHostJson()
                {
                    Enabled = true,
                    Hosts = [host]
                };
            }
            if (newConfig != null)
            {
                _config = newConfig;
            }
            return base.Start();
        }

        public override void Stop()
        {
            _config = null;
            base.Stop();
        }

        public override async Task<GeminiResponse?> Request(Uri url, IPEndPoint client, X509Certificate? cert)
        {
            //Creating a copy now avoids race conditions with Stop()
            var hosts = _config?.Hosts;
            if (hosts == null)
            {
                return null;
            }
            var host = GetHostConfig(url, hosts)
                ?? throw new InvalidOperationException(
                    $"This {nameof(StaticFileHost)} does not accept requests for {url.Host}");

            var _root = host.RootDirectory;
            var _dirBrowse = host.AllowDirectoryBrowsing;

            if (!host.IsAcceptedCertificate(cert))
            {
                _logger.LogWarning("Certificate was not accepted");
                return await Task.FromResult(GeminiResponse.CertificateRequired());
            }

            var p = Path.GetFullPath(Path.Combine(_root, url.LocalPath[1..])).TrimEnd(Path.DirectorySeparatorChar);
            if (p != _root && !p.StartsWith(_root + Path.DirectorySeparatorChar))
            {
                _logger.LogWarning("Possible path traversal attack by {ip}: Path mapped to {path}, which is outside of {root}", client, p, _root);
                return await Task.FromResult(GeminiResponse.BadRequest());
            }
            if (Directory.Exists(p))
            {
                if (_dirBrowse)
                {
                    var di = new DirectoryInfo(p);
                    //Add trailing slash for directory URLs
                    if (!url.LocalPath.EndsWith('/'))
                    {
                        return GeminiResponse.Redirect(url.LocalPath + "/");
                    }

                    _logger.LogInformation("Building directory listing of {path} for {client}", p, client);

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
                _logger.LogInformation("Sending file {file} to {client}", p, client);
                return await Task.FromResult(GeminiResponse.File(p));
            }
            return await Task.FromResult(GeminiResponse.NotFound());
        }

        public override bool IsAccepted(Uri _1, IPAddress _2, X509Certificate? _3)
        {
            var c = _config?.Hosts;
            if (c == null || c.Length == 0)
            {
                return false;
            }
            var host = GetHostConfig(_1, c);
            if (host == null)
            {
                return false;
            }
            return host.IsAcceptedRemoteAddress(_2);
            //Certificate check is not done here to facilitate status code 60
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        private StaticFileHostConfig? GetHostConfig(Uri url, StaticFileHostConfig[]? configs) => GetHostConfig(url.Host, configs);

        private StaticFileHostConfig? GetHostConfig(string host, StaticFileHostConfig[]? configs)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException($"'{nameof(host)}' cannot be null or whitespace.", nameof(host));
            }
            ArgumentNullException.ThrowIfNull(configs);

            _logger.LogDebug("Getting config for {host}", host);
            if (configs.Length == 0)
            {
                return null;
            }
            return configs.FirstOrDefault(m => m.IsMatch(host));
        }
    }
}
