using AyrA.AutoDI;
using Gemini.Lib;
using Gemini.Lib.Services;
using Gemini.Server.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Server
{
    [AutoDIRegister(AutoDIType.Transient)]
    public class GeminiRequestHandler : IDisposable
    {
        private static readonly Uri infoUrl = new("about:info");
        private readonly ILogger<GeminiRequestHandler> _logger;
        private readonly CertificateService _certificateService;
        private readonly IServiceProvider _serverProvider;
        private readonly Type[] _hostTypes;
        private readonly GeminiHost[] _hosts;
        private bool disposed = false;

        public Dictionary<string, X509Certificate2>? ServerCertificates { get; set; }
        public bool RequireClientCertificate { get; set; }

        public GeminiRequestHandler(ILogger<GeminiRequestHandler> logger, CertificateService certificateService, IServiceProvider serverProvider, IServiceCollection services)
        {
            _logger = logger;
            _certificateService = certificateService;
            _serverProvider = serverProvider;
            _hostTypes = Tools.GetHosts(services);
            var hosts = _hostTypes.Select(m => (GeminiHost)serverProvider.GetRequiredService(m)).ToList();
            for (var i = 0; i < hosts.Count; i++)
            {
                var host = hosts[i];
                var name = host.GetType().Name;
                var start = false;
                try
                {
                    _logger.LogInformation("Starting host: {type}", name);
                    start = host.Start();
                    if (!start)
                    {
                        _logger.LogInformation("Host {type} returned 'false' during Start() call. Discarding it", name);
                    }
                }
                catch (Exception ex)
                {
                    start = false;
                    _logger.LogWarning(ex, "Host {type} could not be started due to an error. Discarding it", name);
                }
                if (!start)
                {
                    hosts.RemoveAt(i--);
                    try
                    {
                        host.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, ".Dispose() of {type} threw an exception. This should never happen, and the owner of this module should fix this ASAP.", name);
                    }
                }
            }
            if (hosts.Count == 0)
            {
                _logger.LogWarning("No hosts are active for this request handler. Terminating operations");
                throw new Exception("No hosts are active for this request handler. Terminating operations");
            }
            _hosts = hosts
                .OrderBy(m => m.Priority)
                .ThenBy(m => m.GetType().AssemblyQualifiedName)
                .ThenBy(m => m.GetType().FullName)
                .ToArray();
        }

        [MemberNotNull(nameof(ServerCertificates))]
        public void UseDeveloperCertificate()
        {
            ServerCertificates = new Dictionary<string, X509Certificate2>()
            {
                {"*", _certificateService.CreateOrLoadDevCert() }
            };
        }

        public void TcpServerEventHandler(object sender, Socket client, IPEndPoint remoteAddress)
            => HandleTcpConnection(client, remoteAddress);

        public void HandleTcpConnection(Socket client, IPEndPoint remoteAddress)
        {
            _logger.LogInformation("Got connection from {address}", remoteAddress);
            if (ServerCertificates == null || ServerCertificates.Count == 0)
            {
                _logger.LogWarning("No server certificate was specified. Creating development certificate");
                UseDeveloperCertificate();
            }
            using var tls = _serverProvider.GetRequiredService<TlsServer>();
            tls.RequireClientCertificate = RequireClientCertificate;
            tls.SetConnection(client);
            _logger.LogDebug("Trying TLS server auth with {address}...", remoteAddress);
            try
            {
                tls.ServerAuth(ServerCertificates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TLS authentication failed with {address}", remoteAddress);
                return;
            }
            _logger.LogInformation("TLS auth Ok. Client certificate: {subject}", tls.ClientCertificate?.Subject ?? "<none>");
            using var authStream = tls.GetStream() ?? throw null!;

            _logger.LogDebug("Reading request...");
            Uri? url = null;
            try
            {
                url = ReadRequest(authStream);
                if (url == null)
                {
                    throw new Exception("Failed to decode uri. Aborting request");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Request parsing failed for {address}", remoteAddress);
                try
                {
                    using var br = GeminiResponse.BadRequest("Cannot parse request into a gemini URL. "
                        + GetStatusMessage(ex));
                    br.SendTo(authStream);
                }
                catch (Exception exSendErr)
                {
                    _logger.LogError(exSendErr, "Sending error response to {address} failed", remoteAddress);
                }
            }

            if (url == null)
            {
                return;
            }
            if (url == infoUrl)
            {
                using var infoResponse = GeminiResponse.Ok(@"
[FORM]
multi=n
files=n
stream=n
[META]
extended=y
[BODY]
compress=n
[TCP]
keepalive=n
raw=n
");
                infoResponse.SendTo(authStream);
                return;
            }

            GeminiResponse? response = null;
            _logger.LogInformation("Request URL from {address}: {url}", remoteAddress, url);
            using var scope = _serverProvider.CreateScope();
            for (var i = 0; i < _hosts.Length && response == null; i++)
            {
                _logger.LogDebug("Checking host {index}/{count}", i + 1, _hosts.Length);
                var host = _hosts[i];
                var hostname = host.GetType().FullName;
                bool accepted = false;
                try
                {
                    accepted = host.IsAccepted(url, remoteAddress.Address, tls.ClientCertificate);
                    if (!accepted)
                    {
                        _logger.LogDebug("Host rejected request. Skipping it");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Function .IsAccepted(...) of {host} failed. Skipping it.", hostname);
                    continue;
                }
                if (accepted)
                {
                    try
                    {
                        url = host.Rewrite(url, remoteAddress.Address, tls.ClientCertificate);
                        if (url == null)
                        {
                            _logger.LogInformation("{host} set the url to null. Aborting the request", hostname);
                            return;
                        }
                        response = host.Request(url, remoteAddress, tls.ClientCertificate).Result;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Request processing of {host} for {address} failed", hostname, remoteAddress);
                        using var se = new GeminiResponse(StatusCode.CgiError, null, GetStatusMessage(ex));
                        try
                        {
                            se.SendTo(authStream);
                            _logger.LogInformation("Response: {code} {status}", (int)se.StatusCode, se.Status);
                        }
                        catch (Exception exErr)
                        {
                            _logger.LogWarning(exErr, "Unable to send error response to {address}", remoteAddress);
                        }
                        return;
                    }
                }
                if (response != null)
                {
                    using (response)
                    {
                        _logger.LogInformation("Response: {code} {status}", (int)response.StatusCode, response.Status);
                        try
                        {
                            response.SendTo(authStream);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send response data to {address}", remoteAddress);
                        }
                    }
                    //Stop processing
                    return;
                }
            }
            //No host accepted the request
            _logger.LogInformation("No response from any host. Sending default 'not found' to {address}", remoteAddress);
            try
            {
                using var g404 = GeminiResponse.NotFound();
                g404.SendTo(authStream);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to send error response to {address}", remoteAddress);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            GC.SuppressFinalize(this);
            for (var i = 0; i < _hosts.Length; i++)
            {
                var h = _hosts[i];
                try
                {
                    h.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ".Stop() of {type} threw an exception.", h.GetType().Name);
                }
                try
                {
                    h.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ".Dispose() of {type} threw an exception. This should never happen, and the owner of this module should fix this ASAP.", h.GetType().Name);
                }
            }
        }

        public static Uri ReadRequest(Stream source)
        {
            var bytes = new List<byte>();
            while (true)
            {
                var b = source.ReadByte();
                if (b < 0)
                {
                    throw new IOException("Request ended before a newline could be read");
                }
                if (b == '\r')
                {
                    b = source.ReadByte();
                    if (b == '\n')
                    {
                        if (bytes.Count == 0)
                        {
                            return infoUrl;
                        }
                        return ParseUrl(Encoding.UTF8.GetString(bytes.ToArray()));
                    }
                }
                else if (b == '\n')
                {
                    throw new IOException("Malformed request. Client sent LF but line endings should be CRLF");
                }
                else
                {
                    bytes.Add((byte)b);
                }
            }
        }

        private static Uri ParseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException($"'{nameof(url)}' cannot be null or whitespace.", nameof(url));
            }
            if (!url.ToLower().StartsWith("gemini://"))
            {
                throw new ArgumentException("Client sent non-gemini URL: " + url);
            }
            if (Regex.IsMatch(url, @"[\x00-\x1F]"))
            {
                throw new ArgumentException($"URL contains unescaped control characters");
            }
            return new Uri(url);
        }

        private static string GetStatusMessage(Exception? ex)
        {
            var parts = new List<string>();
            while (ex != null)
            {
                parts.Add(ex.Message);
                ex = ex.InnerException;
            }
            return string.Join(" --> ", parts);
        }

    }
}
