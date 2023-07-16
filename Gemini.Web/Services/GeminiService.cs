using AyrA.AutoDI;
using Gemini.Web.Exceptions;
using Gemini.Web.Models;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Gemini.Web.Services
{
    [AutoDIRegister(AutoDIType.Transient)]
    public class GeminiService
    {
        private const string CRLF = "\r\n";
        public const int DefaultPort = 1965;

        private readonly ILogger<GeminiService> _logger;
        private readonly ServerIdentityService _serverIdentity;

        public GeminiService(ILogger<GeminiService> logger, ServerIdentityService serverIdentity)
        {
            _logger = logger;
            _serverIdentity = serverIdentity;
        }

        public Task<GeminiResponseModel> GetContentAsync(Uri url) => GetContentAsync(url, null);

        public async Task<GeminiResponseModel> GetContentAsync(Uri url, X509Certificate2? clientCertificate)
        {
            var host = url.Host;
            var port = url.IsDefaultPort || url.Port < 1 ? DefaultPort : url.Port;

            if (clientCertificate != null && !clientCertificate.HasPrivateKey)
            {
                _logger.LogError("Supplied client certificate '{subject}' lacks private key",
                    clientCertificate.Subject);
                throw new ArgumentException("Supplied client certificate lacks private key");
            }

            _logger.LogInformation("Requesting {url}", url);

            using var client = new TcpClient();
            try
            {
                var delayTask = Task.Delay(5000);
                if (delayTask == await Task.WhenAny(delayTask, client.ConnectAsync(host, port)))
                {
                    _logger.LogError("Server did not respond to our connection attempts within 5 seconds");
                    throw new TimeoutException("Server did not respond to our connection attempts within 5 seconds");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to {url}", url);
                throw;
            }
            _logger.LogInformation("Connected. Performing SSL handshake");

            bool RemoteCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
            {
                _logger.LogInformation("Server cert validation result: {policy}", sslPolicyErrors);
                if (certificate == null)
                {
                    _logger.LogWarning("Server sent no certificate");
                    return false;
                }
                if (sslPolicyErrors != SslPolicyErrors.None)
                {
                    if (!_serverIdentity.CheckServerTrust($"{host}:{port}", certificate))
                    {
                        throw new UnknownCertificateException("Certificate is not in trust list",
                            new X509Certificate2(certificate.GetRawCertData()));
                    }
                }
                return true;
            }
            X509Certificate clientCertSelect(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate? remoteCertificate, string[] acceptableIssuers)
            {
                if (clientCertificate != null)
                {
                    _logger.LogInformation("Performing client SSL authentication");
                }
                return clientCertificate!;
            }

            using var ssl = new SslStream(
                client.GetStream(), false,
                RemoteCallback, clientCertSelect,
                EncryptionPolicy.RequireEncryption);

            _logger.LogInformation("Performing SSL authentication for {host}", host);
            try
            {
                await ssl.AuthenticateAsClientAsync(host);
                if (!ssl.IsAuthenticated)
                {
                    _logger.LogWarning("SSL authentication failed for {host}", host);
                    throw new Exception($"SSL authentication failed for {host}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSL connection failed: {msg}", ex.Message);
                throw;
            }
            await ssl.WriteAsync(Encoding.UTF8.GetBytes(url.ToString() + CRLF));
            await ssl.FlushAsync();
            var status = GetStatusLine(ssl);

            _logger.LogInformation("Status: {code}; Meta: {meta}", status.StatusCode, status.Meta);

            if (!status.IsSuccess)
            {
                //Stop processing here.
                //No content is expected if it's not a success code
                return status;
            }
            //On success, read body into response
            var data = await ReadToEndAsync(ssl);
            status.Content = status.MimeInformation?.Encoding != null
                ? status.MimeInformation.Encoding.GetString(data)
                : data;
            return status;
        }

        private GeminiResponseModel GetStatusLine(Stream s)
        {
            var line = ReadLine(s, 2048);
            return new GeminiResponseModel(_logger, line);
        }

        private static async Task<byte[]> ReadToEndAsync(Stream s)
        {
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            return ms.ToArray();
        }

        private string ReadLine(Stream s, int limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }
            var bytes = new List<byte>();
            while (bytes.Count < limit)
            {
                var b = s.ReadByte();
                if (b < 0)
                {
                    break;
                }
                if (b == 0x0A)
                {
                    if (bytes.LastOrDefault() == 0x0D)
                    {
                        bytes.RemoveAt(bytes.Count - 1);
                    }
                    break;
                }
                bytes.Add((byte)b);
            }
            if (bytes.Count >= limit)
            {
                _logger.LogWarning("Attempted to read too many bytes (limit was {limit})", limit);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
