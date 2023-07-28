using Gemini.Lib;
using Gemini.Server.Network;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Server
{
    internal class Debugging
    {
        private static readonly ILogger logger = Tools.GetLogger<Debugging>();
        private static X509Certificate2? cert;


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

            if (cert == null)
            {
                cert = Certificates.CreateCertificate("YOLO");
#if DEBUG
                foreach (var host in GeminiHostScanner.Hosts)
                {
                    if (host is StaticFileHost staticHost)
                    {
                        staticHost.RegisterThumbprint(cert.Thumbprint);
                    }
                }
#endif
            }
            try
            {
                var opt = new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                    ApplicationProtocols = new() { new SslApplicationProtocol("GEMINI") },
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    ClientCertificates = new X509CertificateCollection(new X509Certificate[] { cert })
                };
                stream.AuthenticateAsClient(opt);
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
                sw.WriteLine("gemini://example.com/");
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
