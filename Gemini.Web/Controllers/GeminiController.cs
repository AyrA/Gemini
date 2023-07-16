﻿using Gemini.Web.Exceptions;
using Gemini.Web.Models;
using Gemini.Web.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Gemini.Web.Controllers
{
    [ApiController, Route("[controller]/[action]"), EnableCors("API")]
    public class GeminiController : Controller
    {
        private readonly ILogger<GeminiController> _logger;
        private readonly GeminiService _geminiService;

        public GeminiController(ILogger<GeminiController> logger, GeminiService geminiService)
        {
            _logger = logger;
            _geminiService = geminiService;
        }

        /// <summary>
        /// Retrieves a gemini resource
        /// </summary>
        /// <param name="url">gemini URL</param>
        /// <returns>gemini data</returns>
        [HttpPost, Produces("application/json")]
        public async Task<GeminiResponseModel> Navigate([FromForm] Uri url)
        {
            GeminiResponseModel? content;
            _logger.LogInformation("API request for Navigate({url})", url);
            try
            {
                content = await _geminiService.GetContentAsync(url);
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
    }
}
