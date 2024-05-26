using AyrA.AutoDI;
using Gemini.Lib;
using Gemini.Lib.Data;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Plugin
{
    [AutoDIRegister(AutoDIType.Singleton)]
    public class IpAndTimeHost(ILogger<IpAndTimeHost> logger) : GeminiHost
    {
        public override bool IsAccepted(Uri url, IPAddress remoteAddress, X509Certificate? clientCertificate)
        {
            return url != null &&
                (url.PathAndQuery == "/time" || url.PathAndQuery == "/ip");
        }

        public override async Task<GeminiResponse?> Request(Uri url, IPEndPoint clientAddress, X509Certificate? clientCertificate)
        {
            if (url.PathAndQuery == "/ip")
            {
                logger.LogDebug("Answering IP request");
                return await Task.FromResult(GeminiResponse.Ok($"# Your IP address\r\n{clientAddress.Address}"));
            }
            if (url.PathAndQuery == "/time")
            {
                logger.LogDebug("Answering TIME request");
                var time = DateTime.UtcNow.ToString("yyyy.MM.dd HH:mm:ss");
                return await Task.FromResult(GeminiResponse.Ok($"# Current UTC date and time\r\n{time}"));
            }
            return null;
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}