using AyrA.AutoDI;
using Gemini.Lib;
using Gemini.Lib.Services;
using Gemini.Server.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Server
{
    [AutoDIRegister(AutoDIType.Transient)]
    public class GeminiRequestHandler
    {
        private readonly ILogger<GeminiRequestHandler> _logger;
        private readonly CertificateService _certificateService;
        private readonly IServiceProvider _serverProvider;
        private readonly Type[] _hostTypes;

        public X509Certificate2? ServerCertificate { get; set; }

        public GeminiRequestHandler(ILogger<GeminiRequestHandler> logger, CertificateService certificateService, IServiceProvider serverProvider, IServiceCollection services)
        {
            _logger = logger;
            _certificateService = certificateService;
            _serverProvider = serverProvider;
            _hostTypes = Tools.GetHosts(services);
        }

        public void UseDeveloperCertificate()
        {
            ServerCertificate = _certificateService.CreateOrLoadDevCert();
        }

        public void TcpServerEventHandler(object sender, Socket client, IPEndPoint remoteAddress)
            => HandleTcpConnection(client, remoteAddress);

        public void HandleTcpConnection(Socket client, IPEndPoint remoteAddress)
        {
            _logger.LogInformation("Got connection from {address}", remoteAddress);
            if (ServerCertificate == null)
            {
                _logger.LogWarning("No server certificate was specified. Creating development certificate");
                ServerCertificate = _certificateService.CreateOrLoadDevCert();
            }
            using var tls = _serverProvider.GetRequiredService<TlsServer>();
            tls.SetConnection(client);
            _logger.LogDebug("Trying TLS server auth with {address}...", remoteAddress);
            try
            {
                tls.ServerAuth(ServerCertificate);
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Request parsing failed for {address}", remoteAddress);
                try
                {
                    using var br = GeminiResponse.BadRequest("Cannot parse request into a gemini URL");
                    br.SendTo(authStream);
                }
                catch (Exception exSendErr)
                {
                    _logger.LogError(exSendErr, "Sending error response to {address} failed", remoteAddress);
                }
            }

            if (url != null)
            {
                GeminiResponse? response = null;
                _logger.LogInformation("Request URL from {address}: {url}", remoteAddress, url);
                using var scope = _serverProvider.CreateScope();
                for (var i = 0; i < _hostTypes.Length && response == null; i++)
                {
                    _logger.LogDebug("Checking host {index}/{count}", i + 1, _hostTypes.Length);
                    var host = (GeminiHost)_serverProvider.GetRequiredService(_hostTypes[i]);
                    var hostname = host.GetType().FullName;
                    bool accepted = false;
                    try
                    {
                        accepted = host.IsAccepted(url, remoteAddress.Address, tls.ClientCertificate);
                        if (!accepted)
                        {
                            _logger.LogDebug("Host rejected request");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Host function of {host} .IsAccepted(...) failed", hostname);
                    }
                    try
                    {
                        if (accepted)
                        {
                            url = host.Rewrite(url, remoteAddress.Address, tls.ClientCertificate);
                            if (url == null)
                            {
                                _logger.LogInformation("{host} set the url to null", hostname);
                                return;
                            }
                            response = host.Request(url, remoteAddress, tls.ClientCertificate).Result;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Request processing of {host} for {address} failed", hostname, remoteAddress);
                        using var se = new GeminiResponse(StatusCode.CgiError, null, ex.Message);
                        try
                        {
                            se.SendTo(authStream);
                            _logger.LogInformation("Response: {code} {status}", (int)se.StatusCode, se.Status);
                        }
                        catch (Exception exErr)
                        {
                            _logger.LogWarning(exErr, "Unable to send error response to {address}", remoteAddress);
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

    }
}
