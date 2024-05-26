using AyrA.AutoDI;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Gemini.Lib.Services
{
    /// <summary>
    /// Provides means to work with certificates
    /// </summary>
    /// <remarks>
    /// DI
    /// </remarks>
    /// <param name="logger">Logger instance</param>
    [AutoDIRegister(AutoDIType.Singleton)]
    public partial class CertificateService(ILogger<CertificateService> logger)
    {
        /// <summary>
        /// Maximum name length as per X509
        /// </summary>
        public const int MaxNameLength = 64;

        /// <summary>
        /// Encryption parameters for private keys
        /// </summary>
        private static readonly PbeParameters encParams = new(
            PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);

        /// <summary>
        /// Checks if the given certificate time frame is valid
        /// </summary>
        /// <param name="cert">Certificate</param>
        /// <returns>true, if is valid</returns>
        public static bool IsValid(X509Certificate cert) => IsValid((X509Certificate2)cert);

        /// <summary>
        /// Checks if the given certificate time frame is valid
        /// </summary>
        /// <param name="cert">Certificate</param>
        /// <returns>true, if is valid</returns>
        public static bool IsValid(X509Certificate2 cert)
        {
            return
                cert != null
                && cert.NotAfter > DateTime.Now
                && cert.NotBefore < DateTime.Now;
        }

        /// <summary>
        /// Checks if the argument is a valid SHA1 thumbprint
        /// </summary>
        /// <param name="thumbprint">Thumbprint</param>
        /// <returns>true, if valid</returns>
        public static bool IsValidThumbprint(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return false;
            }
            return thumbprint.Length == 40 && HexMatcher().IsMatch(thumbprint);
        }

        /// <summary>
        /// Export the given certificate and key unencrypted to a string
        /// </summary>
        /// <param name="certificate">Certificate to export</param>
        /// <returns>PEM string</returns>
        public string Export(X509Certificate2 certificate) => Export(certificate, null);

        /// <summary>
        /// Exports the given certificate and key with optional encryption to a string
        /// </summary>
        /// <param name="certificate">Certificate</param>
        /// <param name="password">Encryption key for private key</param>
        /// <returns>PEM string</returns>
        public string Export(X509Certificate2 certificate, string? password)
        {
            if (certificate is null)
            {
                logger.LogError("Certificate argument was null");
                throw new ArgumentNullException(nameof(certificate));
            }
            bool encrypt = !string.IsNullOrEmpty(password);
            if (!encrypt)
            {
                logger.LogInformation("Exporting certificate {thumb} unencrypted", certificate.Thumbprint);
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

        /// <summary>
        /// Create a certificate with the given name that is valid for a year
        /// </summary>
        /// <param name="name">Certificate name</param>
        /// <returns>Certificate</returns>
        public X509Certificate2 CreateCertificate(string name) => CreateCertificate(name, null);

        /// <summary>
        /// Creates a certificate with the given name and SAN names that is valid for a year
        /// </summary>
        /// <param name="name">Certificate name</param>
        /// <param name="san">SAN names</param>
        /// <returns>Certificate</returns>
        public X509Certificate2 CreateCertificate(string name, IEnumerable<string>? san) => CreateCertificate(name, san, DateTime.UtcNow.Date.AddYears(1));

        /// <summary>
        /// Creates a certificate that expires at the given date
        /// </summary>
        /// <param name="name">Certificate name</param>
        /// <param name="san">SAN names</param>
        /// <param name="validTo">Expiration date</param>
        /// <returns>Certificate</returns>
        public X509Certificate2 CreateCertificate(string name, IEnumerable<string>? san, DateTime validTo)
            => CreateCertificate(name, san, DateTime.UtcNow.Date, validTo);

        /// <summary>
        /// Creates a certificate that is valid within the given time frame
        /// </summary>
        /// <param name="name">Certificate name</param>
        /// <param name="san">SAN names</param>
        /// <param name="validFrom">Valid from this date</param>
        /// <param name="validTo">Expiration date</param>
        /// <returns>Certificate</returns>
        public X509Certificate2 CreateCertificate(string name, IEnumerable<string>? san, DateTime validFrom, DateTime validTo)
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            return CreateFromKey(name, san, validFrom, validTo, key);
        }

        /// <summary>
        /// Create a certificate using an existing key
        /// </summary>
        /// <param name="name">Certificate name</param>
        /// <param name="san">SAN names</param>
        /// <param name="validFrom">Valid from this date</param>
        /// <param name="validTo">Expiration date</param>
        /// <param name="existingKey">Existing ECDSA key</param>
        /// <returns>Certificate</returns>
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
            ArgumentNullException.ThrowIfNull(existingKey);

            logger.LogInformation("Creating certificate {name} for range {from} --> {to}", name, validFrom, validTo);

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
                        logger.LogWarning("SAN list has empty/whitespace entry. Skipping it");
                        continue;
                    }
                    else if (entry.StartsWith("*.") && Uri.CheckHostName(entry[2..]) == UriHostNameType.Dns)
                    {
                        sanBuilder.AddDnsName(entry);
                        ++proc;
                    }
                    else if (entry.StartsWith("*.") && Uri.CheckHostName(entry[2..]) == UriHostNameType.Basic)
                    {
                        sanBuilder.AddDnsName(entry);
                        ++proc;
                    }
                    else if (IPAddress.TryParse(entry, out var ip))
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
                        logger.LogWarning("No idea how to handle SAN name {name}. Skipping it.", entry);
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

        /// <summary>
        /// Deserializes PEM data into a certificate
        /// </summary>
        /// <param name="pemData">PEM data with certificate and private key</param>
        /// <param name="password">Encryption password, can be null if key is unencrypted</param>
        /// <returns>Certificate</returns>
        public X509Certificate2 ReadFromPemData(string pemData, string? password)
        {
            logger.LogDebug("Trying to decode pem data. Use password: {usepass}", !string.IsNullOrEmpty(password));
            if (pemData.Contains("ENCRYPTED PRIVATE KEY") || pemData.Contains("ENCRYPTED RSA PRIVATE KEY"))
            {
                if (string.IsNullOrEmpty(password))
                {
                    logger.LogError("PEM private key is encrypted but no password supplied");
                    throw new ArgumentException("Private key is encrypted but password is empty", nameof(password));
                }
                using var cert = X509Certificate2.CreateFromEncryptedPem(pemData, pemData, password);
                return MakeExportable(cert);
            }
            else if (!string.IsNullOrEmpty(password))
            {
                logger.LogError("PEM private key is not encrypted but a password was supplied");
                throw new ArgumentException("No encrypted private key was found, but password is specified", nameof(password));
            }
            else if (pemData.Contains("PRIVATE KEY"))
            {
                using var cert = X509Certificate2.CreateFromPem(pemData, pemData);
                return MakeExportable(cert);
            }
            logger.LogError("PEM is lacks private key");
            throw new FormatException("Certificate data lacks a private key");
        }

        /// <summary>
        /// Deserializes a file that contains PKCS12 formatted data or PEM data into a certificate
        /// </summary>
        /// <param name="fileName">File path and name</param>
        /// <param name="password">Encryption password, can be null if key is unencrypted</param>
        /// <returns>Certificate</returns>
        public X509Certificate2 ReadFromFile(string fileName, string? password)
            => ReadFromPemData(File.ReadAllText(fileName), password);

        /// <summary>
        /// Gets the developer certificate. Creates it if necessary
        /// </summary>
        /// <returns>Certificate</returns>
        /// <remarks>
        /// See <see cref="CreateOrLoadDevCert(out bool)"/> for more details
        /// </remarks>
        public X509Certificate2 CreateOrLoadDevCert()
            => CreateOrLoadDevCert(out _);

        /// <summary>
        /// Gets the developer certificate. Creates it if necessary
        /// </summary>
        /// <returns>Certificate</returns>
        /// <param name="created">
        /// true, if the certificate had to be created,
        /// false, if an existing certificate was reused.</param>
        /// <remarks>
        /// The developer certificate is issued for a year,
        /// and contains "localhost", the local host name, and the loopback IP address as SAN names.
        /// The hostnames are also added with wildcard support.
        /// If less than 1/3 of the lifetime is remaining, a new certificate will be created automatically.
        /// </remarks>
        public X509Certificate2 CreateOrLoadDevCert(out bool created)
        {
            X509Certificate2? cert;
            var certFile = Path.Combine(AppContext.BaseDirectory, "development.crt");
            if (File.Exists(certFile))
            {
                using (cert = X509Certificate2.CreateFromPemFile(certFile, certFile))
                {
                    var thirdDuration = cert.NotAfter.Subtract(cert.NotBefore).Ticks / 3;
                    if (cert.NotAfter.ToUniversalTime().AddTicks(-thirdDuration) >= DateTime.UtcNow)
                    {
                        created = false;
                        logger.LogInformation("Reusing existing developer certificate valid until {date}", cert.NotAfter.ToUniversalTime());
                        return MakeExportable(cert);
                    }
                    logger.LogWarning("Developer certificate has expired or has less than 1/3 of its lifetime remaining. Creating a new certificate now");
                }
            }
            logger.LogInformation("Creating developer certificate valid for one year");
            var names = new string[] {
                "localhost",
                "*.localhost",
                IPAddress.IPv6Loopback.ToString(),
                IPAddress.Loopback.ToString(),
                Dns.GetHostName() ?? Environment.MachineName,
                "*." + (Dns.GetHostName() ?? Environment.MachineName)
            };
            using (cert = CreateCertificate("Gemini developer certificate", names, DateTime.Today.AddYears(1)))
            {
                logger.LogInformation("Exporting {name} to {file}", cert.Subject, certFile);
                File.WriteAllText(certFile, Export(cert));
                created = true;
                return MakeExportable(cert);
            }
        }

        /// <summary>
        /// Reads a certificate from the certificate store.
        /// This is a Windows-Only feature.
        /// Throws if the certificate cannot be found
        /// </summary>
        /// <param name="thumbprint">Certificate thumbprint</param>
        /// <returns>Certificate</returns>
        public X509Certificate2 ReadFromStore(string thumbprint)
        {
            if (!OperatingSystem.IsWindows())
            {
                logger.LogError("Tried to call {func} from non-windows platform", nameof(ReadFromStore));
                throw new PlatformNotSupportedException("This function is only available on Windows");
            }
            if (!IsValidThumbprint(thumbprint))
            {
                throw new ArgumentException("Invalid thumbprint value");
            }
            thumbprint = thumbprint.ToUpper();
            logger.LogInformation("Getting certificate {thumbprint} from store", thumbprint);

            var cert =
                GetCert(thumbprint, StoreLocation.CurrentUser)
                ?? GetCert(thumbprint, StoreLocation.LocalMachine)
                ?? throw new ArgumentException($"Certificate with thumbprint {thumbprint} could not be found");
            return MakeExportable(cert);
        }

        /// <summary>
        /// Tries to get a certificate from the given store location
        /// </summary>
        /// <param name="thumbprint">Certificate thumbprint</param>
        /// <param name="location">Store location</param>
        /// <returns>Certificate if found, null otherwise</returns>
        private X509Certificate2? GetCert(string thumbprint, StoreLocation location)
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var cert = store.Certificates.FirstOrDefault(m => m.Thumbprint.Equals(thumbprint, StringComparison.InvariantCultureIgnoreCase));
            store.Close();
            if (cert != null)
            {
                if (!cert.HasPrivateKey)
                {
                    logger.LogWarning("Certificate {thumbprint} found in store {store} but has no private key", thumbprint, location);
                    cert.Dispose();
                    return null;
                }
                logger.LogInformation("Certificate {name} found in store {store}", cert.Subject, location);
                return cert;
            }
            logger.LogInformation("Certificate {thumbprint} not found in store {store}", thumbprint, location);
            return null;
        }

        //Windows Crypto API will not just export every key.
        //Especially keys that were just generated can often not be exported properly.
        //The functions in this section make keys and certificates exportable
        #region Windows Cert+Key Export Hacks

        private static X509Certificate2 MakeExportable(X509Certificate2 cert)
        {
            return new X509Certificate2(cert.Export(X509ContentType.Pkcs12), (string?)null, X509KeyStorageFlags.Exportable);
        }

        private static AsymmetricAlgorithm MakeExportable(AsymmetricAlgorithm key)
        {
            ArgumentNullException.ThrowIfNull(key);

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

        [GeneratedRegex(@"^[\da-fA-F]+$")]
        private static partial Regex HexMatcher();

        #endregion
    }
}
