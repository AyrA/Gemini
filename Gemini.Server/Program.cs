using Gemini.Server;
using Gemini.Server.Network;
using Microsoft.Extensions.Logging;
using System.Net;

internal class Program
{
    private static readonly ILogger logger = Tools.GetLogger<Program>();

    private static void Main(string[] args)
    {
        //Register Gemini URI scheme with the HTTP handler because it's similar.
        //Gemini lacks the URI fragment but we don't care.
        UriParser.Register(new HttpStyleUriParser(), "gemini", 1965);

        logger.LogInformation("Found {count} gemini hosts", GeminiHostScanner.Hosts.Length);

        var server = new TcpServer(IPAddress.Loopback);
        server.Connection += GeminiRequestHandler.Tcp_Handler;
        server.Start();
        logger.LogInformation("Server listening on {endpoint}", server.LocalEndpoint);

        Debugging.DumbClient(new IPEndPoint(IPAddress.Loopback, TcpServer.DefaultPort));
        logger.LogInformation("Server ready. Press CTRL+C to exit");
        Thread.CurrentThread.Join();
    }
}
