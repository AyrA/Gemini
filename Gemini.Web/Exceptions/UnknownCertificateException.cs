using System.Security.Cryptography.X509Certificates;

namespace Gemini.Web.Exceptions
{
    public class UnknownCertificateException : Exception
    {
        public X509Certificate2 Certificate { get; private set; }

        public UnknownCertificateException(string message, X509Certificate2 certificate) : base(message)
        {
            Certificate = certificate;
        }
    }
}
