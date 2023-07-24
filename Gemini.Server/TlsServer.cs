using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Server
{
    public class TlsServer : IDisposable
    {
        private readonly SslStream _stream;

        public X509Certificate2? ClientCertificate { get; private set; }
        public bool RequireClientCertificate { get; set; }

        public TlsServer(Socket socket) : this(new NetworkStream(socket, true)) { }

        public TlsServer(NetworkStream baseStream)
        {
            if (baseStream is null)
            {
                throw new ArgumentNullException(nameof(baseStream));
            }
            _stream = new SslStream(baseStream, false);
        }

        public void ServerAuth(X509Certificate cert)
        {
            if (cert is null)
            {
                throw new ArgumentNullException(nameof(cert));
            }
            var certFix = new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
            var opt = new SslServerAuthenticationOptions()
            {
                AllowRenegotiation = true,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                ServerCertificate = certFix,
                RemoteCertificateValidationCallback = RemoteCert,
                ApplicationProtocols = new() { new SslApplicationProtocol("GEMINI") }
            };
            _stream.AuthenticateAsServer(opt);
            Console.WriteLine(_stream.NegotiatedApplicationProtocol.ToString());
        }

        public void ServerAuth(IDictionary<string, X509Certificate> hostCertList)
        {
            if (hostCertList is null)
            {
                throw new ArgumentNullException(nameof(hostCertList));
            }
            var opt = new SslServerAuthenticationOptions()
            {
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls13,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                RemoteCertificateValidationCallback = RemoteCert,
                ServerCertificateSelectionCallback = LocalCert,
                ApplicationProtocols = new() { new SslApplicationProtocol("GEMINI") }
            };
            _stream.AuthenticateAsServer(opt);
        }

        public Stream GetStream() => _stream;

        public void Dispose()
        {
            Console.WriteLine("Disposing TLS connection");
            GC.SuppressFinalize(this);
            _stream.Dispose();
        }

        private X509Certificate LocalCert(object sender, string? hostName)
        {
            throw new NotImplementedException();
        }

        private bool RemoteCert(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            ClientCertificate = (X509Certificate2?)certificate;
            return certificate != null || !RequireClientCertificate;
        }
    }
}
