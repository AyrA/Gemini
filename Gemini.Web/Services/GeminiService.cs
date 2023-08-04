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
        /// <summary>
        /// State of a gemini service instance
        /// </summary>
        public enum RequestState
        {
            /// <summary>
            /// No request is currently performed
            /// </summary>
            Idle,
            /// <summary>
            /// Trying to connect to a server
            /// </summary>
            Connecting,
            /// <summary>
            /// Server is connected, trying to create SSL connection
            /// </summary>
            InitiatingSsl,
            /// <summary>
            /// Client is checking the server certificate
            /// </summary>
            CheckingServerCertificate,
            /// <summary>
            /// Client is performing client authentication, possibly with a client certificate
            /// </summary>
            ClientAuthentication,
            /// <summary>
            /// Client is sending a gemini request
            /// </summary>
            SendingRequest,
            /// <summary>
            /// Client is reading a gemini response
            /// </summary>
            ReadingStatus,
            /// <summary>
            /// Client is reading gemini content
            /// </summary>
            ReadingContent
        }

        private const string CRLF = "\r\n";
        public const int DefaultPort = 1965;

        /// <summary>
        /// Gets the state of the gemini instance.
        /// This can be used to determine where in the request the client is,
        /// and to provide more accurate error responses to the client,
        /// should an exception be thrown.
        /// </summary>
        public RequestState CurrentState { get; private set; } = RequestState.Idle;

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
            CurrentState = RequestState.Connecting;

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
                CurrentState = RequestState.CheckingServerCertificate;
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
                CurrentState = RequestState.ClientAuthentication;
                if (clientCertificate != null)
                {
                    _logger.LogInformation("Performing client SSL authentication");
                }
                //Windows fix: we cannot use clientCertificate directly
                return localCertificates[0];
            }
            CurrentState = RequestState.InitiatingSsl;
            var hasCert = clientCertificate != null;
            using var ssl = new SslStream(client.GetStream(), false);

            _logger.LogInformation("Performing SSL authentication for {host}", host);
            var opt = new SslClientAuthenticationOptions()
            {
                TargetHost = host,
                ApplicationProtocols = new() { new SslApplicationProtocol("GEMINI") },
                ClientCertificates = hasCert ? new X509CertificateCollection(new[] { clientCertificate! }) : null,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                LocalCertificateSelectionCallback = hasCert ? clientCertSelect : null,
                RemoteCertificateValidationCallback = RemoteCallback
            };
            try
            {
                await ssl.AuthenticateAsClientAsync(opt);
                if (!ssl.IsAuthenticated)
                {
                    _logger.LogWarning("SSL authentication failed for {host}", host);
                    throw new SslException($"SSL authentication failed for {host}");
                }
            }
            catch (SslException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSL authentication failed: {msg}", ex.Message);
                if (clientCertificate != null)
                {
                    throw new SslException($"Failed to perform SSL authentication. Most likely cause is that your certificate '{clientCertificate.Subject}' was rejected by the server.", ex);
                }
                throw new SslException("Failed to perform SSL authentication. Most likely cause is that the server demands a certificate, but is not sending a 6x gemini error code, but closes the connection.", ex);
            }
            CurrentState = RequestState.SendingRequest;
            await ssl.WriteAsync(Encoding.UTF8.GetBytes(url.ToString() + CRLF));
            await ssl.FlushAsync();
            CurrentState = RequestState.ReadingStatus;
            var status = GetStatusLine(ssl);

            _logger.LogInformation("Status: {code}; Meta: {meta}", status.StatusCode, status.Meta);

            if (!status.IsSuccess)
            {
                CurrentState = RequestState.Idle;
                //Stop processing here.
                //No content is expected if it's not a success code
                return status;
            }
            CurrentState = RequestState.ReadingContent;
            //On success, read body into response
            var data = await ReadToEndAsync(ssl);
            status.Content = status.MimeInformation?.Encoding != null
                ? status.MimeInformation.Encoding.GetString(data)
                : data;
            CurrentState = RequestState.Idle;
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
