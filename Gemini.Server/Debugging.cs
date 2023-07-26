using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Gemini.Server
{
    internal class Debugging
    {
        private static readonly ILogger logger = Tools.GetLogger<Debugging>();

        [Conditional("DEBUG")]
        public static void DumbClient(IPEndPoint remote)
        {
            using var c = new TcpClient();
            logger.LogInformation("Connecting to server...");
            try
            {
                c.Connect(remote);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect to {endpoint}", remote);
                return;
            }
            logger.LogInformation("Ok. Tls client auth");
            using var ns = new NetworkStream(c.Client, true);
            using var stream = new SslStream(ns, false);

            try
            {
                stream.AuthenticateAsClient(new SslClientAuthenticationOptions()
                {
                    TargetHost = "localhost",
                    EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                    EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                    ApplicationProtocols = new() { new SslApplicationProtocol("GEMINI") },
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TLS Authentication failed");
                return;
            }

            //Get directory listing document
            using var sr = new StreamReader(stream);
            using var sw = new StreamWriter(stream);
            try
            {
                sw.WriteLine("gemini://127.0.0.1/");
                sw.Flush();
                logger.LogDebug("Response: {data}", sr.ReadToEnd());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gemin irequest failed");
            }
        }
    }
}
