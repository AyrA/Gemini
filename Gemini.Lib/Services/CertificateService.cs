using AyrA.AutoDI;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Gemini.Lib.Services
{
    [AutoDIRegister(AutoDIType.Singleton)]
    public class CertificateService
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
        private readonly ILogger<CertificateService> _logger;

        public CertificateService(ILogger<CertificateService> logger)
        {
            _logger = logger;
        }

        public static bool IsValid(X509Certificate cert) => IsValid((X509Certificate2)cert);

        public static bool IsValid(X509Certificate2 cert)
        {
            return
                cert != null
                && cert.NotAfter > DateTime.Now
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

        public string Export(X509Certificate2 certificate) => Export(certificate, null);

        public string Export(X509Certificate2 certificate, string? password)
        {
            if (certificate is null)
            {
                _logger.LogError("Certificate argument was null");
                throw new ArgumentNullException(nameof(certificate));
            }
            bool encrypt = !string.IsNullOrEmpty(password);
            if (!encrypt)
            {
                _logger.LogInformation("Exporting certificate {thumb} unencrypted", certificate.Thumbprint);
            }

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
                    using var tmpKey = MakeExportable(rsaKey);
                    byte[] privKeyBytes;
                    if (!encrypt)
                    {
                        privKeyBytes = tmpKey.ExportRSAPrivateKey();
                    }
                    else
                    {
                        privKeyBytes = tmpKey.ExportEncryptedPkcs8PrivateKey(password, encParams);
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

        public X509Certificate2 CreateCertificate(string name) => CreateCertificate(name, null);

        public X509Certificate2 CreateCertificate(string name, IEnumerable<string>? san) => CreateCertificate(name, san, DateTime.UtcNow.Date.AddYears(1));

        public X509Certificate2 CreateCertificate(string name, IEnumerable<string>? san, DateTime validTo)
            => CreateCertificate(name, san, DateTime.UtcNow.Date, validTo);

        public X509Certificate2 CreateCertificate(string name, IEnumerable<string>? san, DateTime validFrom, DateTime validTo)
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            return CreateFromKey(name, san, validFrom, validTo, key);
        }

        public X509Certificate2 CreateFromKey(string name, IEnumerable<string>? san, DateTime validFrom, DateTime validTo, ECDsa existingKey)
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
            if (existingKey is null)
            {
                throw new ArgumentNullException(nameof(existingKey));
            }

            _logger.LogInformation("Creating certificate {name} for range {from} --> {to}", name, validFrom, validTo);

            var segments = new string[]
            {
                "CN=" + name,
                "OU=Gemini.Server",
                "O=https://github.com/AyrA/Gemini"
            };

            var dName = new X500DistinguishedName(string.Join("\r\n", segments),
                //Using line breaks here means we don't have to escape other special characters
                X500DistinguishedNameFlags.UseNewLines);
            var req = new CertificateRequest(dName, MakeExportable(existingKey), HashAlgorithmName.SHA256);

            if (san != null)
            {
                var sanBuilder = new SubjectAlternativeNameBuilder();
                var proc = 0;
                foreach (var entry in san.DistinctBy(m => m?.ToLower() ?? ""))
                {
                    if (string.IsNullOrWhiteSpace(entry))
                    {
                        _logger.LogWarning("SAN list has empty/whitespace entry. Skipping it");
                        continue;
                    }
                    if (IPAddress.TryParse(entry, out var ip))
                    {
                        sanBuilder.AddIpAddress(ip);
                        ++proc;
                    }
                    else if (Uri.TryCreate(entry, UriKind.Absolute, out var url))
                    {
                        sanBuilder.AddUri(url);
                        ++proc;
                    }
                    else if (Uri.CheckHostName(entry) != UriHostNameType.Unknown)
                    {
                        sanBuilder.AddDnsName(entry);
                        ++proc;
                    }
                    else if (entry.Contains('@'))
                    {
                        sanBuilder.AddEmailAddress(entry);
                        ++proc;
                    }
                    else
                    {
                        _logger.LogWarning("No idea how to handle SAN name {name}. Skipping it.", entry);
                    }
                }
                //Don't add extensions if we skipped all entries
                if (proc > 0)
                {
                    req.CertificateExtensions.Add(sanBuilder.Build());
                }
            }

            using var cert = req.CreateSelfSigned(validFrom, validTo);
            //Windows fix: Need to export and re-import it or weird crypto API errors start happening.
            return MakeExportable(cert);
        }

        public X509Certificate2 ReadFromPemData(string pemData, string? password)
        {
            _logger.LogDebug("Trying to decode pem data. Use password: {usepass}", !string.IsNullOrEmpty(password));
            if (pemData.Contains("ENCRYPTED PRIVATE KEY") || pemData.Contains("ENCRYPTED RSA PRIVATE KEY"))
            {
                if (string.IsNullOrEmpty(password))
                {
                    _logger.LogError("PEM private key is encrypted but no password supplied");
                    throw new ArgumentException("Private key is encrypted but password is empty", nameof(password));
                }
                using var cert = X509Certificate2.CreateFromEncryptedPem(pemData, pemData, password);
                return MakeExportable(cert);
            }
            else if (!string.IsNullOrEmpty(password))
            {
                _logger.LogError("PEM private key is not encrypted but a password was supplied");
                throw new ArgumentException("No encrypted private key was found, but password is specified", nameof(password));
            }
            else if (pemData.Contains("PRIVATE KEY"))
            {
                using var cert = X509Certificate2.CreateFromPem(pemData, pemData);
                return MakeExportable(cert);
            }
            _logger.LogError("PEM is lacks private key");
            throw new FormatException("Certificate data lacks a private key");
        }

        public X509Certificate2 ReadFromFile(string fileName, string? password)
            => ReadFromPemData(File.ReadAllText(fileName), password);

        public X509Certificate2 CreateOrLoadDevCert()
            => CreateOrLoadDevCert(out _);

        public X509Certificate2 CreateOrLoadDevCert(out bool created)
        {
            var certFile = Path.Combine(AppContext.BaseDirectory, "server.crt");
            if (File.Exists(certFile))
            {
                created = false;
                _logger.LogInformation("Reusing existing developer certificate");
                return X509Certificate2.CreateFromPemFile(certFile, certFile);
            }
            _logger.LogInformation("Creating developer certificate valid for one year");
            var names = new string[] {
                "localhost",
                IPAddress.IPv6Loopback.ToString(),
                IPAddress.Loopback.ToString(),
                Dns.GetHostName() ?? Environment.MachineName
            };
            var cert = CreateCertificate("Gemini developer certificate", names, DateTime.Today.AddYears(1));

            File.WriteAllText(certFile, Export(cert));
            created = true;
            return cert;
        }

        #region Windows Hacks

        private static X509Certificate2 MakeExportable(X509Certificate2 cert)
        {
            return new X509Certificate2(cert.Export(X509ContentType.Pkcs12), (string?)null, X509KeyStorageFlags.Exportable);
        }

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

        #endregion
    }
}
