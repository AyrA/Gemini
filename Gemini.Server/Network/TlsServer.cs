using AyrA.AutoDI;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Server.Network
{
    [AutoDIRegister(AutoDIType.Transient)]
    public class TlsServer : IDisposable
    {
        private SslStream? _stream;
        private readonly ILogger _logger;

        public X509Certificate2? ClientCertificate { get; private set; }

        public bool RequireClientCertificate { get; set; }

        public TlsServer(ILogger<TlsServer> logger)
        {
            _logger = logger;
        }

        public void SetConnection(Socket s)
            => SetConnection(new NetworkStream(s, true));

        public void SetConnection(NetworkStream baseStream)
        {
            if (baseStream is null)
            {
                _logger.LogError("Constructor called with with null argument");
                throw new ArgumentNullException(nameof(baseStream));
            }
            if (!baseStream.CanRead || !baseStream.CanWrite)
            {
                _logger.LogError("Stream argument not read-writable");
                throw new ArgumentException("Stream not read-writable", nameof(baseStream));
            }

            _stream = new SslStream(baseStream, false);
        }

        public void ServerAuth(X509Certificate cert)
        {
            if (cert is null)
            {
                _logger.LogError("ServerAuth called with with null argument");
                throw new ArgumentNullException(nameof(cert));
            }
            if (_stream is null)
            {
                throw new InvalidOperationException("Stream has not been created");
            }
            _logger.LogInformation("Authenticate as TLS server");
            //Fix Windows Bug
            var certFix = new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
            var opt = new SslServerAuthenticationOptions()
            {
                AllowRenegotiation = true,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                ServerCertificate = certFix,
                RemoteCertificateValidationCallback = ProcessClientCert,
                ApplicationProtocols = new() { new SslApplicationProtocol("GEMINI") }
            };
            _stream.AuthenticateAsServer(opt);
            _logger.LogDebug("Chosen ALPN: {alpn}", _stream.NegotiatedApplicationProtocol.ToString());
        }

        public void ServerAuth(IDictionary<string, X509Certificate> hostCertList)
        {
            if (hostCertList is null)
            {
                throw new ArgumentNullException(nameof(hostCertList));
            }
            if (hostCertList.Count == 0)
            {
                throw new ArgumentException("Certificate list cannot be empty");
            }
            if (_stream is null)
            {
                throw new InvalidOperationException("Stream has not been created");
            }

            //Select a certificate from hostCertList based on the host name
            X509Certificate localCert(object sender, string? hostName)
            {
                _logger.LogDebug("Trying to find best certificate for {hostname}", hostName);
                //If the host name is null, try to return the default certificate,
                //otherwise return the first one
                if (hostName == null)
                {
                    _logger.LogWarning("Client did not send SNI host name");
                    if (hostCertList.TryGetValue("*", out var defaultCert))
                    {
                        return defaultCert;
                    }
                    return hostCertList.First().Value;
                }
                if (
                    hostCertList.TryGetValue(hostName.ToUpper(), out var cert) ||
                    hostCertList.TryGetValue("*", out cert))
                {
                    _logger.LogInformation("Chosen certificate: {subject}", cert.Subject);
                    return cert;
                }
                _logger.LogInformation("Host name not found. Using first certificate");
                return hostCertList.First().Value;
            }
            var opt = new SslServerAuthenticationOptions()
            {
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls13,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                RemoteCertificateValidationCallback = ProcessClientCert,
                ServerCertificateSelectionCallback = localCert,
                ApplicationProtocols = new() { new SslApplicationProtocol("GEMINI") }
            };
            _stream.AuthenticateAsServer(opt);
            _logger.LogDebug("Chosen ALPN: {alpn}", _stream.NegotiatedApplicationProtocol.ToString());
        }

        public Stream? GetStream() => _stream;

        public void Dispose()
        {
            _logger.LogDebug("Disposing TLS connection");
            _stream?.Dispose();
            ClientCertificate?.Dispose();
            GC.SuppressFinalize(this);
        }

        private bool ProcessClientCert(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            ClientCertificate = (X509Certificate2?)certificate;
            if (ClientCertificate != null)
            {
                _logger.LogInformation("Got client certificate: {subject}", ClientCertificate.Subject);
            }
            return certificate != null || !RequireClientCertificate;
        }
    }
}
