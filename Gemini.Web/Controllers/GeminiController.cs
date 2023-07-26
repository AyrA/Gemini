using Gemini.Web.Enums;
using Gemini.Web.Exceptions;
using Gemini.Web.Models;
using Gemini.Web.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Gemini.Web.Controllers
{
    [ApiController, Route("[controller]/[action]"), EnableCors("API")]
    public class GeminiController : Controller
    {
        private readonly ILogger<GeminiController> _logger;
        private readonly GeminiService _geminiService;
        private readonly CertificateProviderService _certificateService;

        public GeminiController(ILogger<GeminiController> logger, GeminiService geminiService, CertificateProviderService certificateService)
        {
            _logger = logger;
            _geminiService = geminiService;
            _certificateService = certificateService;
        }

        /// <summary>
        /// Retrieves a gemini resource
        /// </summary>
        /// <param name="url">gemini URL</param>
        /// <param name="certificate">Id of the client identity to use. Anonymous if not supplied</param>
        /// <param name="password">Password of the client identity to use. Assumes unencrypted id if not supplied</param>
        /// <returns>gemini data</returns>
        [HttpPost, Produces("application/json")]
        public async Task<GeminiResponseModel> Navigate([FromForm] Uri url, [FromForm] string? certificate, [FromForm] string? password)
        {
            GeminiResponseModel? content;
            _logger.LogInformation("API request for Navigate({url})", url);
            try
            {
                X509Certificate2? cert = null;
                if (!string.IsNullOrEmpty(certificate))
                {
                    cert = _certificateService.GetCertificate(certificate, password).GetCertificate();
                }
                content = await _geminiService.GetContentAsync(url, cert);
            }
            catch (UnknownCertificateException ex)
            {
                content = new GeminiResponseModel(GeminiResponseModel.InternalErrors.UnknownCertificate,
                    "application/pkcs8")
                {
                    Content = new
                    {
                        Host = $"{url.Host}:{(url.Port <= 0 ? GeminiService.DefaultPort : url.Port)}",
                        SubjectName = ex.Certificate.Subject,
                        Id = ex.Certificate.Thumbprint.ToUpper(),
                        IssuerName = ex.Certificate.Issuer,
                        Expires = ex.Certificate.NotAfter.ToUniversalTime(),
                        Certificate = ex.Certificate.GetRawCertData()
                    }
                };
            }
            catch (Exception ex)
            {
                content = new GeminiResponseModel(GeminiResponseModel.InternalErrors.GenericError,
                    "PROTOCOL VIOLATION")
                {
                    Content = Encoding.UTF8.GetBytes(ex.Message)
                };
            }
            return content;
        }

        /// <summary>
        /// Retrieves a gemini resource and delivers it as if it came from an HTTP server
        /// </summary>
        /// <param name="url">gemini URL</param>
        /// <param name="certificate">Id of the client identity to use. Anonymous if not supplied</param>
        /// <param name="password">
        /// Password of the client identity to use. Assumes unencrypted id if not supplied
        /// </param>
        /// <returns>gemini data</returns>
        /// <remarks>
        /// Supplying passwords via URL is usually not recommended. Use this function with care.
        /// </remarks>
        [HttpGet]
        public async Task<IActionResult> Get(Uri url, string? certificate, string? password)
        {
            _logger.LogInformation("API request for Get({url})", url);
            try
            {
                X509Certificate2? cert = null;
                if (!string.IsNullOrEmpty(certificate))
                {
                    cert = _certificateService.GetCertificate(certificate, password).GetCertificate();
                }
                var content = await _geminiService.GetContentAsync(url, cert);
                switch ((StatusCode)content.StatusCode)
                {
                    case Enums.StatusCode.Input:
                        return BadRequest("This resource requires user input. " + content.Meta);
                    case Enums.StatusCode.SensitiveInput:
                        return BadRequest("This resource requires secret input (Password etc.). " + content.Meta);
                    case Enums.StatusCode.Success:
                        HttpContext.Response.ContentType = content.Meta;
                        switch (content.ContentType)
                        {
                            case ContentType.Unknown:
                                return StatusCode(500, "Unknown gemini response content type");
                            case ContentType.String:
                                await HttpContext.Response.Body.WriteAsync(Encoding.UTF8.GetBytes((string?)content.Content ?? string.Empty));
                                return new EmptyResult();
                        }
                        await HttpContext.Response.Body.WriteAsync((byte[]?)content.Content ?? Array.Empty<byte>());
                        return new EmptyResult();
                    case Enums.StatusCode.TemporaryRedirect:
                    case Enums.StatusCode.PermanentRedirect:
                        return RedirectToAction(nameof(Get), new { url = new Uri(url, content.Meta), certificate, password });
                    case Enums.StatusCode.TemporaryFailure:
                        return StatusCode(500, "Temporary Gemini server error. " + content.Meta);
                    case Enums.StatusCode.ServerUnavailable:
                        return StatusCode(503, "Gemini server unavailable error. " + content.Meta);
                    case Enums.StatusCode.CgiError:
                        return StatusCode(502, "Gemini server CGI error. " + content.Meta);
                    case Enums.StatusCode.ProxyError:
                        return StatusCode(502, "Gemini server proxy error. " + content.Meta);
                    case Enums.StatusCode.SlowDown:
                        HttpContext.Response.Headers.RetryAfter = content.Meta;
                        return StatusCode(429, $"Slow Down. Try again no sooner than {content.Meta} seconds.");
                    case Enums.StatusCode.PermanentFailure:
                        return BadRequest("Gemini server sent permanent error code. " + content.Meta);
                    case Enums.StatusCode.NotFound:
                        return NotFound(content.Meta);
                    case Enums.StatusCode.Gone:
                        return StatusCode(410, content.Meta);
                    case Enums.StatusCode.ProxyRequestRefused:
                        return StatusCode(403, "Proxy request refused. " + content.Meta);
                    case Enums.StatusCode.BadRequest:
                        return BadRequest(content.Meta);
                    case Enums.StatusCode.ClientCertificateRequired:
                        HttpContext.Response.Headers.WWWAuthenticate = "Gemini-Certificate";
                        return Unauthorized("Client certificate required. " + content.Meta);
                    case Enums.StatusCode.CertificateNotAuthorized:
                        return StatusCode(403, "Client certificate not authorized. " + content.Meta);
                    case Enums.StatusCode.CertificateNotValid:
                        return BadRequest("Client certificate not valid. " + content.Meta);
                }
                return StatusCode(500, $"Backend sent invalid status line: {content.StatusCode} {content.Meta}");
            }
            catch (UnknownCertificateException ex)
            {
                return BadRequest("The supplied certificate cannot be found or decoded. " + ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Unable to retrieve content from the backend. " + ex.Message);
            }
        }
    }
}
