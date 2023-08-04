using System.Security.Cryptography.X509Certificates;

namespace Gemini.Web.Models
{
    public class CertificateInfo : IDisposable
    {
        private readonly X509Certificate2 _cert;

        public string FriendlyName => _cert.Subject;
        public string Id => _cert.Thumbprint.ToUpper();
        public DateTime ValidFrom => _cert.NotBefore.ToUniversalTime();
        public DateTime ValidUntil => _cert.NotAfter.ToUniversalTime();

        public bool Encrypted { get; set; }

        internal CertificateInfo(X509Certificate2 cert, bool encrypted = false)
        {
            _cert = cert;
            Encrypted = encrypted;
        }

        /// <summary>
        /// Gets public certificate information regardless of whether the private key is encrypted or not
        /// </summary>
        /// <param name="certPath">Certificate file</param>
        /// <returns>Certificate information structure</returns>
        public static CertificateInfo PublicOnly(string certPath)
        {
            var certData = File.ReadAllLines(certPath);
            return new CertificateInfo(X509Certificate2.CreateFromPem(string.Join("\r\n", certData)))
            {
                Encrypted = certData.Any(m => m.Contains("ENCRYPTED PRIVATE KEY"))
            };
        }

        /// <summary>
        /// Import a certificate from existing, decoded data
        /// </summary>
        /// <param name="cert">Certificate</param>
        /// <returns>Certificate info</returns>
        public static CertificateInfo Import(X509Certificate2 cert) => new(cert);

        public X509Certificate2 GetCertificate()
        {
            return _cert;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _cert.Dispose();
        }
    }
}
