using AyrA.AutoDI;
using Gemini.Lib.Services;
using Gemini.Web.Models;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Web.Services
{
    [AutoDIRegister(AutoDIType.Scoped)]
    public class CertificateProviderService : IDisposable
    {
        private readonly List<CertificateInfo> _issuedInstances = new();
        private readonly string certDir;
        private readonly CertificateService _certService;

        public CertificateProviderService(CertificateService certService)
        {
            certDir = Path.Combine(AppContext.BaseDirectory, "Cert");
            Directory.CreateDirectory(certDir);
            _certService = certService;
        }

        public string[] GetCertificateNames()
        {
            return Directory
                .EnumerateFiles(certDir, "*.pem")
                .Select(m => Path.GetFileNameWithoutExtension(m))
                .ToArray();
        }

        public string GetRawCertificate(string id)
        {
            CheckId(id);
            var p = Path.Combine(certDir, id + ".pem");
            return File.ReadAllText(p);
        }

        public CertificateInfo GetPublicCertificate(string id)
        {
            CheckId(id);
            var p = Path.Combine(certDir, id + ".pem");
            var ci = CertificateInfo.PublicOnly(p);
            lock (_issuedInstances)
            {
                _issuedInstances.Add(ci);
            }
            return ci;
        }

        public CertificateInfo GetCertificate(string id, string? password)
        {
            CheckId(id);
            var p = Path.Combine(certDir, id + ".pem");
            var cert = _certService.ReadFromFile(p, password);
            var ci = new CertificateInfo(cert, !string.IsNullOrEmpty(password));
            lock (_issuedInstances)
            {
                _issuedInstances.Add(ci);
            }
            return ci;
        }

        public CertificateInfo CreateNew(string name, string? password, DateTime expiration)
        {
            var cert = _certService.CreateCertificate(name, null, expiration);
            var ci = new CertificateInfo(cert, !string.IsNullOrEmpty(password));
            lock (_issuedInstances)
            {
                _issuedInstances.Add(ci);
            }
            SaveCertificate(ci, password);
            return ci;
        }

        public CertificateInfo Update(string id, string newName, string? password, DateTime expiration)
        {
            CheckId(id);
            var p = Path.Combine(certDir, id + ".pem");
            using var oldCert = _certService.ReadFromFile(p, password);
            var key = oldCert.GetECDsaPrivateKey() ?? throw new Exception("Cannot load key from file");
            var createDate = DateTime.UtcNow.Date;
            createDate = createDate.AddDays(-(createDate.Day - 1));

            var cert = _certService.CreateFromKey(newName, null, createDate, expiration, key);

            var newCert = new CertificateInfo(cert, !string.IsNullOrEmpty(password));
            lock (_issuedInstances)
            {
                _issuedInstances.Add(newCert);
            }
            SaveCertificate(newCert, password);
            DeleteCertificate(id);
            return newCert;
        }

        public CertificateInfo UpdatePassword(string id, string? oldPassword, string? newPassword)
        {
            CheckId(id);
            var ci = GetCertificate(id, oldPassword);
            SaveCertificate(ci, newPassword);
            return ci;
        }

        public CertificateInfo Import(byte[] certificateData, string? password)
        {
            if (certificateData is null)
            {
                throw new ArgumentNullException(nameof(certificateData));
            }

            X509Certificate2? cert;

            if (IsPemCert(certificateData))
            {
                if (string.IsNullOrEmpty(password) && IsEncryptedPem(certificateData))
                {
                    throw new Exception("Supplied PEM certificate contains an encrypted key but no password was supplied.");
                }
                //Try PEM import
                try
                {
                    var pemData = Encoding.UTF8.GetString(certificateData);
                    cert = string.IsNullOrEmpty(password)
                        ? X509Certificate2.CreateFromPem(pemData, pemData)
                        : X509Certificate2.CreateFromEncryptedPem(pemData, pemData, password);
                    if (!cert.HasPrivateKey)
                    {
                        cert.Dispose();
                        throw new CryptographicException("No private key present");
                    }
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        throw new CryptographicException("Unable to decode certificate. Check if the file is a valid PEM formatted certificate", ex);
                    }
                    else
                    {
                        throw new CryptographicException("Unable to decode certificate using the given password. Check if the file is a valid PEM formatted certificate and key and double check the password.", ex);
                    }
                }
            }
            else
            {
                //Try PKCS12 import
                if (string.IsNullOrEmpty(password))
                {
                    throw new ArgumentException("The certificate is not in PEM format. To attempt a PKCS12 import, the password is mandatory. Due to a limitation of the .NET framework, you cannot import a PKCS12 container without a password. Please either change the password of the container, or convert it to PKCS8 PEM format.", nameof(password));
                }
                try
                {
                    cert = new X509Certificate2(certificateData, password,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
                    if (!cert.HasPrivateKey)
                    {
                        cert.Dispose();
                        throw new CryptographicException("No private key present");
                    }
                }
                catch (Exception ex)
                {
                    throw new CryptographicException("Unable to decode certificate as PKCS12. Check that it is a valid PKCS12 formatted file and double check the password", ex);
                }
            }

            if (HasCertificate(cert.Thumbprint))
            {
                using (cert)
                {
                    throw new InvalidOperationException($"A certificate with id {cert.Thumbprint} already exists");
                }
            }

            var ci = CertificateInfo.Import(cert);
            lock (_issuedInstances)
            {
                _issuedInstances.Add(ci);
            }
            SaveCertificate(ci, password);
            return ci;
        }

        public bool DeleteCertificate(string id)
        {
            CheckId(id);
            var p = Path.Combine(certDir, id + ".pem");
            try
            {
                File.Delete(p);
            }
            catch
            {
                //NOOP
            }
            return !File.Exists(p);
        }

        private bool HasCertificate(string id)
        {
            var p = Path.Combine(certDir, id + ".pem");
            return File.Exists(p);
        }

        private void SaveCertificate(CertificateInfo ci, string? password)
        {
            var p = Path.Combine(certDir, ci.Id + ".pem");
            File.WriteAllText(p, _certService.Export(ci.GetCertificate(), password));
            ci.Encrypted = !string.IsNullOrEmpty(password);
        }

        private static void CheckId([NotNull] string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException($"'{nameof(id)}' cannot be null or whitespace.", nameof(id));
            }
            if (id.Length != 40 || !Regex.IsMatch(id, @"^[\dA-Fa-f]+$"))
            {
                throw new FormatException("Bad name format");
            }
        }

        private static bool IsPemCert(byte[] data)
        {
            using var ms = new MemoryStream(data, false);
            using var sr = new StreamReader(ms, Encoding.Latin1, false);
            var line = (string?)null;
            while ((line = sr.ReadLine()) != null)
            {
                if (Regex.IsMatch(line, @"^\s*-+\s*BEGIN\s+CERTIFICATE\s*-+\s*$", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsEncryptedPem(byte[] data)
        {
            using var ms = new MemoryStream(data, false);
            using var sr = new StreamReader(ms, Encoding.Latin1, false);
            var line = (string?)null;
            while ((line = sr.ReadLine()) != null)
            {
                if (Regex.IsMatch(line, @"^\s*-+\s*BEGIN\s+ENCRYPTED\s+PRIVATE\s+KEY\s*-+\s*$", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            lock (_issuedInstances)
            {
                _issuedInstances.ForEach(v => v.Dispose());
                _issuedInstances.Clear();
            }
        }
    }
}
