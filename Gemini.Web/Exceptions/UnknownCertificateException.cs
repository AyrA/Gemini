using System.Security.Cryptography.X509Certificates;

namespace Gemini.Web.Exceptions
{
    public class UnknownCertificateException(string message, X509Certificate2 certificate) : Exception(message)
    {
        public X509Certificate2 Certificate { get; private set; } = certificate;
    }
}
