using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Server
{
    public class Tools
    {
        private static readonly ILoggerFactory loggerFactory;
        private static readonly ILogger logger;

        static Tools()
        {
            //This is how you get access to a logger without all the DI nonsense
            loggerFactory = LoggerFactory.Create(builder =>
            {
#if DEBUG
                builder.SetMinimumLevel(LogLevel.Debug);
#endif
                builder.AddConsole();
            });
            logger = loggerFactory.CreateLogger<Tools>();
        }

        public static ILogger<T> GetLogger<T>()
        {
            return loggerFactory.CreateLogger<T>();
        }

        public static X509Certificate2 CreateOrLoadDevCert()
        {
            var certFile = Path.Combine(AppContext.BaseDirectory, "server.crt");
            if (File.Exists(certFile))
            {
                logger.LogInformation("Reusing existing developer certificate");
                return X509Certificate2.CreateFromPemFile(certFile, certFile);
            }
            logger.LogInformation("Creating developer certificate valid for one year");
            var req = new CertificateRequest(
                "CN=localhost, OU=Gemini.Server, O=https://github.com/AyrA/Gemini",
                RSA.Create(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var cert = req.CreateSelfSigned(DateTime.Today, DateTime.Today.AddYears(1));

            byte[] certificateBytes = cert.RawData;
            char[] certificatePem = PemEncoding.Write("CERTIFICATE", certificateBytes);

            AsymmetricAlgorithm key = cert.GetRSAPrivateKey()
                ?? (AsymmetricAlgorithm?)cert.GetDSAPrivateKey()
                ?? (AsymmetricAlgorithm?)cert.GetECDsaPrivateKey()
                ?? (AsymmetricAlgorithm?)cert.GetECDiffieHellmanPrivateKey()
                ?? throw null!;
            byte[] pubKeyBytes = key.ExportSubjectPublicKeyInfo();
            byte[] privKeyBytes = key.ExportPkcs8PrivateKey();
            char[] pubKeyPem = PemEncoding.Write("PUBLIC KEY", pubKeyBytes);
            char[] privKeyPem = PemEncoding.Write("PRIVATE KEY", privKeyBytes);
            using var sw = File.CreateText(certFile);
            sw.WriteLine(certificatePem);
            sw.WriteLine(pubKeyPem);
            sw.WriteLine(privKeyPem);
            return cert;
        }
    }
}
