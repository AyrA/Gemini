using AyrA.AutoDI;
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

        public CertificateProviderService()
        {
            certDir = Path.Combine(AppContext.BaseDirectory, "Cert");
            Directory.CreateDirectory(certDir);
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
            var ci = new CertificateInfo(p, password);
            lock (_issuedInstances)
            {
                _issuedInstances.Add(ci);
            }
            return ci;
        }

        public CertificateInfo CreateNew(string name, string? password, DateTime expiration)
        {
            var ci = CertificateInfo.Create(name, expiration);
            lock (_issuedInstances)
            {
                _issuedInstances.Add(ci);
            }
            var p = Path.Combine(certDir, ci.Id + ".pem");
            ci.Save(p, password);
            return ci;
        }

        public CertificateInfo Update(string id, string newName, string? password, DateTime expiration)
        {
            CheckId(id);
            var pOld = Path.Combine(certDir, id + ".pem");
            var ci = new CertificateInfo(pOld, password);
            var newCert = ci.Update(newName, expiration);
            var pNew = Path.Combine(certDir, ci.Id + ".pem");
            newCert.Save(pNew, password);
            DeleteCertificate(id);
            return newCert;
        }

        public CertificateInfo Import(byte[] certificateData, string password)
        {
            if (certificateData is null)
            {
                throw new ArgumentNullException(nameof(certificateData));
            }

            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException($"'{nameof(password)}' cannot be null or empty.", nameof(password));
            }

            X509Certificate2? cert;

            if (IsPemCert(certificateData))
            {
                //Try PEM import
                try
                {
                    var pemData = Encoding.UTF8.GetString(certificateData);
                    cert = X509Certificate2.CreateFromEncryptedPem(pemData, pemData, password);
                    if (!cert.HasPrivateKey)
                    {
                        cert.Dispose();
                        throw new CryptographicException("No private key present");
                    }
                }
                catch (Exception ex)
                {
                    throw new CryptographicException("Unable to decode certificate either as pfx/pkcs12 or PEM format", ex);
                }
            }
            else
            {
                //Try PKCS12 import
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
                    throw new CryptographicException("Unable to decode certificate as PKCS12", ex);
                }
            }

            var ci = CertificateInfo.Import(cert);
            lock (_issuedInstances)
            {
                _issuedInstances.Add(ci);
            }
            var p = Path.Combine(certDir, ci.Id + ".pem");
            ci.Save(p, password);
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
