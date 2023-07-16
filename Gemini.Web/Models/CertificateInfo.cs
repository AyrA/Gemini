using System.Security.Cryptography;
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

        public bool Encrypted { get; private set; }

        private CertificateInfo(X509Certificate2 cert)
        {
            _cert = cert;
            Encrypted = false;
        }

        /// <summary>
        /// Load a certificate file
        /// </summary>
        /// <param name="certPath">Certificate file</param>
        /// <param name="password">Certificate file password</param>
        public CertificateInfo(string certPath, string? password)
        {
            if (!string.IsNullOrEmpty(password))
            {
                _cert = X509Certificate2.CreateFromEncryptedPemFile(certPath, password);
                Encrypted = true;
            }
            else
            {
                _cert = X509Certificate2.CreateFromPemFile(certPath);
                Encrypted = false;
            }
        }

        /// <summary>
        /// Creates a new self signed certificate
        /// </summary>
        /// <param name="certDisplayName">Name on the certificate</param>
        /// <param name="expiration">Date of expiration</param>
        public static CertificateInfo Create(string certDisplayName, DateTime expiration)
        {
            if (string.IsNullOrEmpty(certDisplayName))
            {
                throw new ArgumentException($"'{nameof(certDisplayName)}' cannot be null or empty.", nameof(certDisplayName));
            }
            expiration = expiration.ToUniversalTime();
            if (expiration <= DateTime.UtcNow)
            {
                throw new ArgumentOutOfRangeException(nameof(expiration));
            }

            //Do not disclose when exactly the certificate was made
            //Always have it start at midnight UTC on the first of the current month
            var createDate = DateTime.UtcNow.Date;
            createDate = createDate.AddDays(-(createDate.Day - 1));

            return new CertificateInfo(GetCert(certDisplayName, ECDsa.Create(), createDate, expiration));
        }

        /// <summary>
        /// Create a new certificate
        /// </summary>
        /// <param name="certDisplayName">Name on the certificate</param>
        /// <param name="validity">Validity period</param>
        public static CertificateInfo Create(string certDisplayName, TimeSpan validity)
            => Create(certDisplayName, DateTime.UtcNow.Date.Add(validity));

        /// <summary>
        /// Gets public certificate information regardlwss of whether the private key is encrypted or not
        /// </summary>
        /// <param name="certPath">Certificate file</param>
        /// <returns>Certificate information structure</returns>
        public static CertificateInfo PublicOnly(string certPath)
        {
            return new CertificateInfo(X509Certificate2.CreateFromPem(File.ReadAllText(certPath)));
        }

        /// <summary>
        /// Import a certificate from existing, decoded data
        /// </summary>
        /// <param name="cert">Certificate</param>
        /// <returns>Certificate info</returns>
        public static CertificateInfo Import(X509Certificate2 cert) => new(cert);

        /// <summary>
        /// Saves the certificate to a file
        /// </summary>
        /// <param name="certPath"></param>
        /// <param name="password"></param>
        /// <remarks>See: https://stackoverflow.com/a/43941142</remarks>
        public void Save(string certPath, string? password)
        {
            File.WriteAllText(certPath, Export(password));
            Encrypted = !string.IsNullOrEmpty(password);
        }

        /// <summary>
        /// Update existing certificate with new information
        /// </summary>
        /// <param name="certDisplayName">Certificate display name</param>
        /// <param name="expiration">Expiration date</param>
        /// <returns>New instance</returns>
        /// <remarks>
        /// This is like creating a new certificate, except it reuses the key from an existing certificate
        /// </remarks>
        public CertificateInfo Update(string certDisplayName, DateTime expiration)
        {
            if (string.IsNullOrEmpty(certDisplayName))
            {
                throw new ArgumentException($"'{nameof(certDisplayName)}' cannot be null or empty.", nameof(certDisplayName));
            }
            expiration = expiration.ToUniversalTime();
            if (expiration <= DateTime.UtcNow)
            {
                throw new ArgumentOutOfRangeException(nameof(expiration));
            }

            //Do not disclose when exactly the certificate was made
            //Always have it start at midnight UTC on the first of the current month
            var createDate = DateTime.UtcNow.Date;
            createDate = createDate.AddDays(-(createDate.Day - 1));

            return new CertificateInfo(GetCert(certDisplayName, _cert.GetECDsaPrivateKey()!, createDate, expiration));
        }

        /// <summary>
        /// Exports a certificate into a PEM structure
        /// </summary>
        /// <param name="password">Export password. If null, the private key will not be encrypted</param>
        /// <returns>PEM data</returns>
        public string Export(string? password)
        {
            byte[] certificateBytes = _cert.RawData;
            char[] certificatePem = PemEncoding.Write("CERTIFICATE", certificateBytes);

            AsymmetricAlgorithm key = GetAsymmetricAlgorithmKey();
            byte[] pubKeyBytes = key.ExportSubjectPublicKeyInfo();
            byte[] privKeyBytes = GetKeyBytes(key, password);
            char[] pubKeyPem = PemEncoding.Write("PUBLIC KEY", pubKeyBytes);
            char[] privKeyPem = PemEncoding.Write(
                (string.IsNullOrEmpty(password) ? "" : "ENCRYPTED ") + "PRIVATE KEY",
                privKeyBytes);
            using var sw = new StringWriter();
            sw.WriteLine(certificatePem);
            sw.WriteLine(pubKeyPem);
            sw.WriteLine(privKeyPem);
            sw.Flush();
            return sw.ToString();
        }

        private static X509Certificate2 GetCert(string name, ECDsa key, DateTime created, DateTime expires)
        {
            var req = new CertificateRequest($"CN={name},OU=Gemini.Web,O=https://github.com/AyrA/Gemini", key, HashAlgorithmName.SHA256);
            return req.CreateSelfSigned(created, expires);
        }

        private AsymmetricAlgorithm GetAsymmetricAlgorithmKey()
        {
            AsymmetricAlgorithm? key;
            key = _cert.GetRSAPrivateKey();
            if (key != null)
            {
                return key;
            }
            key = _cert.GetECDsaPrivateKey();
            if (key != null)
            {
                return key;
            }
            key = _cert.GetDSAPrivateKey();
            if (key != null)
            {
                return key;
            }
            key = _cert.GetECDiffieHellmanPrivateKey();
            if (key != null)
            {
                return key;
            }
            throw new InvalidOperationException("Certificate has no known private key that this system supports");
        }

        private static byte[] GetKeyBytes(AsymmetricAlgorithm key, string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return key.ExportPkcs8PrivateKey();
            }
            var encryptionParams = new PbeParameters(
                PbeEncryptionAlgorithm.Aes256Cbc,
                HashAlgorithmName.SHA256,
                500_000);
            return key.ExportEncryptedPkcs8PrivateKey(password, encryptionParams);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _cert.Dispose();
        }
    }
}
