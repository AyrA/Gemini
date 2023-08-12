using System.Net;
using System.Net.Security;
using System.Net.Sockets;

//Register gemini
UriParser.Register(new HttpStyleUriParser(), "gemini", 1965);
while (true)
{
    //Ask for URL
    Console.Write("URL: ");
    var url = new Uri(new Uri("gemini://localhost/"), Console.ReadLine());

    //Connect to server
    using var cli = new TcpClient();
    cli.Connect(Dns.GetHostAddresses(url.DnsSafeHost).First(), url.Port);

    //Do SSL
    using var ns = cli.GetStream();
    using var ssl = new SslStream(ns, false);
    ssl.AuthenticateAsClient(new SslClientAuthenticationOptions()
    {
        RemoteCertificateValidationCallback = delegate { return true; },
        EncryptionPolicy = EncryptionPolicy.RequireEncryption,
        TargetHost = url.DnsSafeHost
    });

    //Send request and read response
    using var sw = new StreamWriter(ssl);
    using var sr = new StreamReader(ssl);
    sw.NewLine = "\r\n";
    sw.AutoFlush = true;
    sw.WriteLine(url);
    Console.WriteLine(sr.ReadToEnd());
}