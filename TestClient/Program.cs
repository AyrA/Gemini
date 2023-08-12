using System.Net;
using System.Net.Security;
using System.Net.Sockets;

//Register gemini
UriParser.Register(new HttpStyleUriParser(), "gemini", 1965);
while (true)
{
    //Ask for URL
    Console.Write("URL: ");
    var host = "localhost";
    var port = 1965;
    var request = "";
    var input = Console.ReadLine();
    if (!string.IsNullOrEmpty(input))
    {
        var url = new Uri(new Uri("gemini://localhost/"), input);
        host = url.DnsSafeHost;
        port = url.Port;
        request = url.ToString();
    }

    //Connect to server
    using var cli = new TcpClient();
    cli.Connect(Dns.GetHostAddresses(host).First(), port);

    //Do SSL
    using var ns = cli.GetStream();
    using var ssl = new SslStream(ns, false);
    ssl.AuthenticateAsClient(new SslClientAuthenticationOptions()
    {
        RemoteCertificateValidationCallback = delegate { return true; },
        EncryptionPolicy = EncryptionPolicy.RequireEncryption,
        TargetHost = host
    });

    //Send request and read response
    using var sw = new StreamWriter(ssl);
    using var sr = new StreamReader(ssl);
    sw.NewLine = "\r\n";
    sw.AutoFlush = true;
    sw.WriteLine(request);
    Console.WriteLine(sr.ReadToEnd());
}