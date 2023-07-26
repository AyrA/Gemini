using Gemini.Lib;
using Gemini.Server.Network;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Server
{
    public class GeminiRequestHandler
    {
        private static readonly ILogger logger = Tools.GetLogger<GeminiRequestHandler>();
        public static X509Certificate2? ServerCertificate { get; set; }

        public static void Tcp_Handler(object sender, Socket client, IPEndPoint remoteAddress)
        {
            logger.LogInformation("Got connection from {address}", remoteAddress);
            if (ServerCertificate == null)
            {
                logger.LogWarning("No server certificate was specified. Creating development certificate");
                ServerCertificate = Tools.CreateOrLoadDevCert();
            }
            using var tls = new TlsServer(client);
            logger.LogDebug("Trying TLS server auth with {address}...", remoteAddress);
            try
            {
                tls.ServerAuth(ServerCertificate);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TLS authentication failed with {address}", remoteAddress);
                return;
            }
            logger.LogInformation("TLS auth Ok. Client certificate: {subject}", tls.ClientCertificate?.Subject ?? "<none>");
            using var authStream = tls.GetStream();

            logger.LogDebug("Reading request...");
            Uri? url = null;
            try
            {
                url = ReadRequest(authStream);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Request parsing failed for {address}", remoteAddress);
                try
                {
                    using var br = GeminiResponse.BadRequest("Cannot parse request into a gemini URL");
                    br.SendTo(authStream);
                }
                catch (Exception exSendErr)
                {
                    logger.LogError(exSendErr, "Sending error response to {address} failed", remoteAddress);
                }
            }

            if (url != null)
            {
                GeminiResponse? response = null;
                var hosts = GeminiHostScanner.Hosts;
                logger.LogInformation("Request URL for {address}: {url}", remoteAddress, url);
                for (var i = 0; i < hosts.Length && response == null; i++)
                {
                    var host = hosts[i];
                    var hostname = host.GetType().FullName;
                    bool accepted = false;
                    try
                    {
                        accepted = host.IsAccepted(url, remoteAddress.Address, tls.ClientCertificate);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Host function of {host} .IsAccepted(...) failed", hostname);
                    }
                    try
                    {
                        if (accepted)
                        {
                            url = host.Rewrite(url, remoteAddress.Address, tls.ClientCertificate);
                            if (url == null)
                            {
                                logger.LogInformation("{host} set the url to null", hostname);
                                return;
                            }
                            response = host.Request(url, remoteAddress, tls.ClientCertificate).Result;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Request processing of {host} for {address} failed", hostname, remoteAddress);
                        using var se = new GeminiResponse(StatusCode.CgiError, null, ex.Message);
                        try
                        {
                            se.SendTo(authStream);
                            logger.LogInformation("Response: {code} {status}", (int)se.StatusCode, se.Status);
                        }
                        catch (Exception exErr)
                        {
                            logger.LogWarning(exErr, "Unable to send error response to {address}", remoteAddress);
                        }
                    }
                    if (response != null)
                    {
                        using (response)
                        {
                            logger.LogInformation("Response: {code} {status}", (int)response.StatusCode, response.Status);
                            try
                            {
                                response.SendTo(authStream);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Failed to send response data to {address}", remoteAddress);
                            }
                        }
                    }
                    else
                    {
                        logger.LogInformation("No response. Sending default 404 to {address}", remoteAddress);
                        //No host accepted the request
                        try
                        {
                            using var g404 = GeminiResponse.NotFound();
                            g404.SendTo(authStream);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Unable to send error response to {address}", remoteAddress);
                        }
                    }
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
