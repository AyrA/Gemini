using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Gemini
{
    public static class ContentRetriever
    {
        private const string CRLF = "\r\n";

        public static async Task GetContentAsync(Uri url, int maxRedirects)
        {
            var host = url.Host;
            var port = url.IsDefaultPort ? 1965 : url.Port;

            LogInfo("Requesting {0}", url);

            using var client = new TcpClient();
            try
            {
                var delayTask = Task.Delay(5000);
                if (delayTask == await Task.WhenAny(delayTask, client.ConnectAsync(host, port)))
                {
                    LogErr("Server did not respond to our connection attempts within 5 seconds");
                    return;
                }
            }
            catch (Exception ex)
            {
                LogErr("Failed to connect: [{0}] {1}", ex.GetType().Name, ex.Message);
                return;
            }
            using var ssl = new SslStream(
                client.GetStream(), false,
                RemoteCallback, LocalCertSelect,
                EncryptionPolicy.RequireEncryption);
            try
            {
                await ssl.AuthenticateAsClientAsync(host);
            }
            catch (Exception ex)
            {
                LogErr("SSL failed: [{0}] {1}", ex.GetType().Name, ex.Message);
                return;
            }
            await ssl.WriteAsync(Encoding.UTF8.GetBytes(url.ToString() + CRLF));
            await ssl.FlushAsync();
            var status = GetStatusLine(ssl);

            LogInfo("Status: '{0}'; Meta: '{1}'", status.Code, status.Meta);
            if (status.Code >= 30 && status.Code < 40)
            {
                Uri? newLocation = null;
                try
                {
                    if (maxRedirects == 0)
                    {
                        LogErr("Maximum redirection count exceeded");
                    }
                    else
                    {
                        newLocation = new Uri(url, status.Meta);
                    }
                }
                catch
                {
                    LogWarn("Invalid redirection: {0}", status.Meta);
                }
                if (newLocation != null)
                {
                    await DiscardAsync(ssl);
                    ssl.Close();
                    LogInfo("Following redirect to {0}", newLocation);
                    await GetContentAsync(newLocation, maxRedirects - 1);
                    return;
                }
            }
            Console.WriteLine();
            var data = await ReadToEndAsync(ssl);

            //Try to extract charset from success code
            var charset = (string?)null;
            Encoding? encoding = null;
            if (status.Code >= 20 && status.Code < 30)
            {
                //Fallback to text/gemini if no meta is provided
                var meta = string.IsNullOrWhiteSpace(status.Meta) ? "text/gemini; charset=UTF-8" : status.Meta;
                var parts = meta.Split(';').Select(m => m.Trim()).ToArray();

                //Do not extract codepage if it's not a text type
                if (parts[0].StartsWith("text/"))
                {
                    if (parts.Length > 1)
                    {
                        var r = new Regex(@"^\s*charset\s*=\s*(\S+)\s*$");
                        var parsed = parts.FirstOrDefault(m => r.IsMatch(m));
                        if (parsed != null)
                        {
                            charset = r.Match(parsed).Groups[1].Value;
                        }
                    }
                    //If encoding is not set, assume UTF-8
                    charset ??= "UTF-8";
                    try
                    {
                        encoding = Encoding.GetEncoding(charset);
                    }
                    catch (Exception ex)
                    {
                        LogErr("Unable to parse '{0}' into an encoding. Fallback to UTF-8", charset);
                        LogErr("Error: [{0}] {1}", ex.GetType().Name, ex.Message);
                        encoding = Encoding.UTF8;
                    }
                }
            }
            if (encoding != null)
            {
                LogInfo("Decoding content as '{0}' from '{1}'", encoding.EncodingName, charset);
                Console.WriteLine(encoding.GetString(data));
            }
            else
            {
                Tools.DumpHex(data);
            }
            ssl.Close();
        }

        private static X509Certificate LocalCertSelect(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate? remoteCertificate, string[] acceptableIssuers)
        {
            var cert = localCertificates.OfType<X509Certificate>().FirstOrDefault();
            LogInfo("Server is requesting a client certificate");
            if (cert == null)
            {
                LogInfo("No certificate found");
            }
            else
            {
                LogInfo("Using {0}", cert.Subject);
            }
            return cert!;
        }

        private static bool RemoteCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            LogInfo("Server cert validation result: {0}", sslPolicyErrors);
            return true;
        }

        private static StatusLine GetStatusLine(Stream s)
        {
            var line = ReadLine(s, 2048);
            if (line != null && line.Length > 1 && int.TryParse(line[..2], out int code) && code >= 10 && code < 60)
            {
                return new StatusLine(code, line.Length > 3 ? line[3..] : null);
            }
            LogErr("Protocol violation: Invalid status line. (Was '{0}')", line ?? "<null>");
            return new StatusLine(99, line);
        }

        private static async Task<byte[]> ReadToEndAsync(Stream s)
        {
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            return ms.ToArray();
        }

        private static async Task DiscardAsync(Stream s)
        {
            await s.CopyToAsync(Stream.Null);
        }

        private static string ReadLine(Stream s, int limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }
            var bytes = new List<byte>();
            while (bytes.Count < limit)
            {
                var b = s.ReadByte();
                if (b < 0)
                {
                    break;
                }
                if (b == 0x0A)
                {
                    if (bytes.LastOrDefault() == 0x0D)
                    {
                        bytes.RemoveAt(bytes.Count - 1);
                    }
                    break;
                }
                bytes.Add((byte)b);
            }
            if (bytes.Count >= limit)
            {
                LogWarn("Attempted to read too many bytes (limit was {0})", limit);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static void LogErr(string format, params object?[] args)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERR: " + format, args);
            Console.ResetColor();
        }

        private static void LogWarn(string format, params object?[] args)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WRN: " + format, args);
            Console.ResetColor();
        }

        private static void LogInfo(string format, params object?[] args)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("NFO: " + format, args);
            Console.ResetColor();
        }

    }

    public record StatusLine(int Code, string? Meta);
}
