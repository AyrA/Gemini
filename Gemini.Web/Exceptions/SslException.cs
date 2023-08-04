using System.Runtime.Serialization;

namespace Gemini.Web.Exceptions
{
    public class SslException : Exception
    {
        public SslException()
        {
        }

        public SslException(string? message) : base(message)
        {
        }

        public SslException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected SslException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
