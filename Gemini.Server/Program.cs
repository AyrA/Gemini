using Gemini.Lib;
using Gemini.Server;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

internal class Program
{
    private static readonly List<GeminiHost> hosts = new() { new StaticFileHost("C:\\Temp", true) };
    private static X509Certificate2? certificate;

    private static void Main(string[] args)
    {
        //Register Gemini URI scheme with the HTTP handler because it's good enough
        //Gemini lacks the URI fragment but we don't care.
        UriParser.Register(new HttpStyleUriParser(), "gemini", 1965);

        certificate = CreateDevCert();

        var server = new TcpServer(IPAddress.Loopback);
        server.Connection += Server_Connection;
        hosts[0].Start();
        server.Start();
        Console.WriteLine("Server listening on {0}", server.LocalEndpoint);

        DumbClient();
        Console.WriteLine("Done... Press CTRL+C to exit");
        Thread.CurrentThread.Join();
    }

    private static X509Certificate2 CreateDevCert()
    {
        Console.WriteLine("Creating developer certificate valid for one year");
        var req = new CertificateRequest(
            "CN=localhost, OU=Gemini.Server, O=https://github.com/AyrA/Gemini",
            RSA.Create(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTime.Today, DateTime.Today.AddYears(1));
    }

    private static void Server_Connection(object sender, Socket client, IPEndPoint remoteAddress)
    {
        Console.WriteLine("Got connection from {0}", remoteAddress);
        using var tls = new TlsServer(client);
        Console.WriteLine("Trying TLS server auth...");
        try
        {
            tls.ServerAuth(certificate ?? throw null!);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Server authentication failed. {0}", ex.Message);
            return;
        }
        Console.WriteLine("Ok. Client certificate: {0}", tls.ClientCertificate?.Subject ?? "<none>");
        using var authStream = tls.GetStream();
        Console.WriteLine("Reading request...");
        Uri? url = null;
        try
        {
            url = GeminiRequestReader.ReadRequest(authStream);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Request parsing failed. {0}", ex.Message);
            try
            {
                using var br = GeminiResponse.BadRequest("Cannot parse request into a gemini URL");
                br.SendTo(authStream);
            }
            catch (Exception exSendErr)
            {
                Console.WriteLine("Sending error response failed. {0}", exSendErr.Message);
            }
        }
        if (url != null)
        {
            Console.WriteLine("Ok. URL: {0}", url);
            try
            {
                foreach (var host in hosts)
                {
                    if (host.IsAccepted(url, remoteAddress.Address))
                    {
                        url = host.Rewrite(url, remoteAddress.Address);
                        if (url == null)
                        {
                            Console.WriteLine("Early termination. {0} set the url to null", host.GetType().Name);
                            return;
                        }
                        var response = host.Request(url, remoteAddress).Result;
                        if (response != null)
                        {
                            using (response)
                            {
                                Console.WriteLine("==> {0} {1}", (int)response.StatusCode, response.Status);
                                response.SendTo(authStream);
                            }
                            return;
                        }
                    }
                }
                //No host accepted the request
                using (var response = GeminiResponse.NotFound())
                {
                    response.SendTo(authStream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Request processing failed. {0}", ex.Message);
                using var se = new GeminiResponse(StatusCode.CgiError, null, ex.Message);
                try
                {
                    se.SendTo(authStream);
                    Console.WriteLine("==> {0} {1}", (int)se.StatusCode, se.Status);
                }
                catch
                {
                    Console.WriteLine("Unable to send error response");
                }
            }
        }
    }

    static void DumbClient()
    {
        using var c = new TcpClient();
        Console.WriteLine("Connecting to server...");
        c.Connect(IPAddress.Loopback, TcpServer.DefaultPort);
        Console.WriteLine("Ok. Tls client auth");
        using var ns = new NetworkStream(c.Client, true);
        using var stream = new SslStream(ns, false);

        stream.AuthenticateAsClient(new SslClientAuthenticationOptions()
        {
            TargetHost = "localhost",
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            ApplicationProtocols = new() { new SslApplicationProtocol("GEMINI") },
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        });
        using var sw = new StreamWriter(stream);
        sw.WriteLine("gemini://example.com/?query#fragment");
        sw.Flush();
        using var sr = new StreamReader(stream);
        Console.WriteLine(sr.ReadToEnd());
    }
}
