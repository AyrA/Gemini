using System.Text;
using System.Text.RegularExpressions;

namespace Gemini.Server
{
    public class GeminiRequestReader
    {
        public static Uri ReadRequest(Stream source)
        {
            var bytes = new List<byte>();
            while (true)
            {
                var b = source.ReadByte();
                if (b < 0)
                {
                    throw new IOException("Request ended before a newline could be read");
                }
                if (b == '\r')
                {
                    b = source.ReadByte();
                    if (b == '\n')
                    {
                        return ParseUrl(Encoding.UTF8.GetString(bytes.ToArray()));
                    }
                }
                else if (b == '\n')
                {
                    throw new IOException("Malformed request. Client sent LF but line endings should be CRLF");
                }
                else
                {
                    bytes.Add((byte)b);
                }
            }
        }

        private static Uri ParseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException($"'{nameof(url)}' cannot be null or whitespace.", nameof(url));
            }
            if (!url.ToLower().StartsWith("gemini://"))
            {
                throw new ArgumentException("Client sent non-gemini URL: " + url);
            }
            if (Regex.IsMatch(url, @"[\s\x00-\x1F]"))
            {
                throw new ArgumentException($"URL contains unescaped whitespace or control characters");
            }
            return new Uri(url);
        }
    }
}
