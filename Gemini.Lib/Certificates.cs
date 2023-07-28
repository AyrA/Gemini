using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Gemini.Lib
{
    public static class Certificates
    {
        /// <summary>
        /// Setting this to false falls back to RSA certificates
        /// </summary>
        private static readonly bool useEcc = true;

        /// <summary>
        /// Maximum name length as per X509
        /// </summary>
        public const int MaxNameLength = 64;

        private static readonly PbeParameters encParams = new(
            PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);

        public static bool IsValid(X509Certificate cert) => IsValid((X509Certificate2)cert);

        public static bool IsValid(X509Certificate2 cert)
        {
            if (cert == null)
            {
                return false;
            }
            return
                cert.NotAfter > DateTime.Now
                && cert.NotBefore < DateTime.Now;
        }

        public static bool IsValidThumbprint(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return false;
            }
            return thumbprint.Length == 40 && Regex.IsMatch(thumbprint, @"^[\da-fA-F]+$");
        }

        public static string Export(X509Certificate2 certificate) => Export(certificate, null);

        public static string Export(X509Certificate2 certificate, string? password)
        {
            if (certificate is null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }
            bool encrypt = !string.IsNullOrEmpty(password);

            using var sw = new StringWriter();

            //Export certificate
            byte[] certificateBytes = certificate.RawData;
            sw.WriteLine(PemEncoding.Write("CERTIFICATE", certificateBytes));

            //Export plain public key
            byte[] pubKeyBytes = certificate.GetPublicKey();
            sw.WriteLine(PemEncoding.Write("PUBLIC KEY", pubKeyBytes));

            //Export private key if it has been specified
            if (certificate.HasPrivateKey)
            {
                AsymmetricAlgorithm key = certificate.GetRSAPrivateKey()
                    ?? (AsymmetricAlgorithm?)certificate.GetECDsaPrivateKey()
                    ?? (AsymmetricAlgorithm?)certificate.GetDSAPrivateKey()
                    ?? (AsymmetricAlgorithm?)certificate.GetECDiffieHellmanPrivateKey()
                    ?? throw null!;
                //RSA requires extra work
                if (key is RSA rsaKey)
                {
                    byte[] privKeyBytes;
                    if (!encrypt)
                    {
                        using var tempKey = MakeExportable(rsaKey);
                        privKeyBytes = tempKey.ExportRSAPrivateKey();
                    }
                    else
                    {
                        privKeyBytes = rsaKey.ExportEncryptedPkcs8PrivateKey(password, encParams);
                    }

                    sw.WriteLine(PemEncoding.Write(encrypt ? "ENCRYPTED RSA PRIVATE KEY" : "RSA PRIVATE KEY", privKeyBytes));
                }
                else
                {
                    using var tmpKey = MakeExportable(key);
                    byte[] privKeyBytes = encrypt
                        ? tmpKey.ExportEncryptedPkcs8PrivateKey(password, encParams)
                        : tmpKey.ExportPkcs8PrivateKey();
                    sw.WriteLine(PemEncoding.Write(encrypt ? "ENCRYPTED PRIVATE KEY" : "PRIVATE KEY", privKeyBytes));
                }
            }
            return sw.ToString();
        }

        public static X509Certificate2 CreateCertificate(string name) => CreateCertificate(name, DateTime.UtcNow.Date.AddYears(1));

        public static X509Certificate2 CreateCertificate(string name, DateTime validTo)
            => CreateCertificate(name, DateTime.UtcNow.Date, validTo);

        public static X509Certificate2 CreateCertificate(string name, DateTime validFrom, DateTime validTo)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace.", nameof(name));
            }
            if (name.Length > MaxNameLength)
            {
                throw new ArgumentOutOfRangeException(nameof(name), "Name exceeds permitted maximum length");
            }
            if (name.Contains('\r') || name.Contains('\n'))
            {
                throw new FormatException("Name cannot contain line breaks");
            }
            if (validFrom > validTo || validTo < DateTime.UtcNow)
            {
                throw new ArgumentOutOfRangeException(nameof(validTo));
            }

            var segments = new string[]
            {
                "CN=" + name,
                "OU=Gemini.Server",
                "O=https://github.com/AyrA/Gemini"
            };

            var dName = new X500DistinguishedName(string.Join("\r\n", segments),
                //Using line breaks here means we don't have to escape other special characters
                X500DistinguishedNameFlags.UseNewLines);
            var req = useEcc
                ? new CertificateRequest(dName, ECDsa.Create(ECCurve.NamedCurves.nistP384), HashAlgorithmName.SHA256)
                : new CertificateRequest(dName, RSA.Create(4096), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = req.CreateSelfSigned(validFrom, validTo);
            //Windows fix: Need to export and re-import it or weird crypto API errors start happening.
            return new X509Certificate2(cert.Export(X509ContentType.Pkcs12), (string?)null,
              X509KeyStorageFlags.Exportable);
        }

        public static X509Certificate2 ReadFromPemData(string pemData, string? password)
        {
            if (pemData.Contains("ENCRYPTED PRIVATE KEY"))
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new ArgumentException("Private key is encrypted but password is empty", nameof(password));
                }
                using var cert = X509Certificate2.CreateFromEncryptedPem(pemData, pemData, password);
                return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
            }
            else if (!string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("No encrypted private key was found, but password is specified", nameof(password));
            }
            else if (pemData.Contains("PRIVATE KEY"))
            {
                using var cert = X509Certificate2.CreateFromPem(pemData, pemData);
                return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
            }
            throw new FormatException("Certificate data lacks a private key");
        }

        public static X509Certificate2 ReadFromFile(string fileName, string? password)
            => ReadFromPemData(File.ReadAllText(fileName), password);

        private static AsymmetricAlgorithm MakeExportable(AsymmetricAlgorithm key)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (key is RSA rsaKey)
            {
                return MakeExportable(rsaKey);
            }
            if (key is ECDsa ecdsaKey)
            {
                return MakeExportable(ecdsaKey);
            }
            if (key is DSA dsaKey)
            {
                return MakeExportable(dsaKey);
            }
            if (key is ECDiffieHellman ecdiffieHellmanKey)
            {
                return MakeExportable(ecdiffieHellmanKey);
            }
            throw new NotImplementedException($"Unknown key type: {key.GetType()}");
        }

        private static RSA MakeExportable(RSA key)
        {
            string pwd = nameof(MakeExportable) + key.GetHashCode();
            using RSA tmp = RSA.Create();
            var pbeParameters = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);
            tmp.ImportEncryptedPkcs8PrivateKey(pwd, key.ExportEncryptedPkcs8PrivateKey(pwd, pbeParameters), out _);
            var exportParams = tmp.ExportParameters(true);
            return RSA.Create(exportParams);
        }

        private static DSA MakeExportable(DSA key)
        {
            string pwd = nameof(MakeExportable) + key.GetHashCode();
            using DSA tmp = DSA.Create();
            var pbeParameters = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);
            tmp.ImportEncryptedPkcs8PrivateKey(pwd, key.ExportEncryptedPkcs8PrivateKey(pwd, pbeParameters), out _);
            var exportParams = tmp.ExportParameters(true);
            return DSA.Create(exportParams);
        }

        private static ECDsa MakeExportable(ECDsa key)
        {
            var pwd = nameof(MakeExportable) + key.GetHashCode();
            using ECDsa tmp = ECDsa.Create();
            var pbeParameters = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);
            tmp.ImportEncryptedPkcs8PrivateKey(pwd, key.ExportEncryptedPkcs8PrivateKey(pwd, pbeParameters), out _);
            var exportParams = tmp.ExportParameters(true);
            return ECDsa.Create(exportParams);
        }

        private static ECDiffieHellman MakeExportable(ECDiffieHellman key)
        {
            var pwd = nameof(MakeExportable) + key.GetHashCode();
            using ECDiffieHellman tmp = ECDiffieHellman.Create();
            var pbeParameters = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);
            tmp.ImportEncryptedPkcs8PrivateKey(pwd, key.ExportEncryptedPkcs8PrivateKey(pwd, pbeParameters), out _);
            var exportParams = tmp.ExportParameters(true);
            return ECDiffieHellman.Create(exportParams);
        }
    }
}
