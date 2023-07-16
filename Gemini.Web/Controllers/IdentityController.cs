using Gemini.Web.Models;
using Gemini.Web.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Gemini.Web.Controllers
{
    /// <summary>
    /// Handles authentication of the current user against a gemini server
    /// </summary>
    [ApiController, Route("[controller]/[action]/{id}"), EnableCors("API")]
    public class IdentityController : Controller
    {
        private readonly CertificateProviderService _certificateProvider;

        public IdentityController(CertificateProviderService certificateProvider)
        {
            _certificateProvider = certificateProvider;
        }

        /// <summary>
        /// Get the list of all certificates
        /// </summary>
        /// <returns>Certificate list</returns>
        [HttpGet, Produces("application/json"), Route("/[controller]/[action]")]
        public CertificateInfo[] CertificateList()
        {
            return _certificateProvider
                .GetCertificateNames()
                .Select(m => _certificateProvider.GetPublicCertificate(m))
                .ToArray();
        }

        /// <summary>
        /// Gets the information of a single certificate
        /// </summary>
        /// <param name="id">Certificate id</param>
        /// <returns>Certificate</returns>
        [HttpGet, ActionName("Certificate"), Produces("application/json", Type = typeof(CertificateInfo))]
        public IActionResult CertificateGet(string id)
        {
            try
            {
                return Json(_certificateProvider.GetPublicCertificate(id));
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (DirectoryNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Creates a new certificate
        /// </summary>
        /// <param name="displayName">Certificate name (also shown to remote servers)</param>
        /// <param name="password">Optional private key password. Recommended</param>
        /// <param name="expiration">Expiration date</param>
        /// <returns>Created certificate resource</returns>
        [HttpPost]
        [ActionName("Certificate")]
        [Produces("application/json", Type = typeof(CertificateInfo))]
        [Route("/[controller]/[action]")]
        public IActionResult CertificatePost([FromForm] string displayName, [FromForm] string? password, [FromForm] DateTime expiration)
        {
            var exp = expiration.ToLocalTime().ToUniversalTime().Date;
            try
            {
                return Json(_certificateProvider.CreateNew(displayName, password, exp));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Import an existing certificate
        /// </summary>
        /// <param name="certificate">Certificate</param>
        /// <param name="password">Password</param>
        /// <returns>Imported certificate resource</returns>
        [HttpPut]
        [ActionName("Certificate")]
        [Produces("application/json", Type = typeof(CertificateInfo))]
        [Consumes("multipart/form-data")]
        [Route("/[controller]/[action]")]
        public IActionResult CertificatePut(IFormFile certificate, [FromForm] string password)
        {
            if (certificate is null)
            {
                return BadRequest("No file provided");
            }
            if (certificate.Length > 1024 * 10)
            {
                return BadRequest("Certificate too large.");
            }
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException($"'{nameof(password)}' cannot be null or empty.", nameof(password));
            }

            try
            {
                using var ms = new MemoryStream();
                certificate.CopyToAsync(ms);
                return Json(_certificateProvider.Import(ms.ToArray(), password));
            }
            catch (Exception ex)
            {
                return BadRequest($"Certificate or password is invalid. {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes a certificate
        /// </summary>
        /// <param name="id">Certificate id</param>
        /// <returns>true, if deleted or id doesn't exists, false otherwise</returns>
        [HttpDelete, ActionName("Certificate"), Produces("application/json", Type = typeof(bool))]
        public IActionResult CertificateDelete(string id)
        {
            try
            {
                return Json(_certificateProvider.DeleteCertificate(id));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Updates values of a certificate
        /// </summary>
        /// <param name="id">Certificate id</param>
        /// <param name="password">Existing private key password</param>
        /// <param name="displayName">New display name</param>
        /// <param name="expiration">New expiration date</param>
        /// <returns>Updated certificate</returns>
        /// <remarks>
        /// It's not actually possible to change these values.
        /// What this does is it creates a new certificate using the existing private key,
        /// then it deletes the old certificate.
        /// A server that identifies the client by key will continue to correctly identify this new certificate.
        /// A server that uses the thumbprint (which is incorrect to do so)
        /// will no longer recognize the certificate.
        /// </remarks>
        [HttpPatch, ActionName("Certificate"), Produces("application/json", Type = typeof(CertificateInfo))]
        public IActionResult CertificatePatch(
            string id,
            [FromForm] string? password,
            [FromForm] string displayName,
            [FromForm] DateTime expiration)
        {
            var exp = expiration.ToLocalTime().ToUniversalTime().Date;
            try
            {
                return Json(_certificateProvider.Update(id, displayName, password, exp));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Export a certificate as PEM data
        /// </summary>
        /// <param name="id">Certificate id</param>
        /// <returns>Certificate PEM data</returns>
        [HttpGet, Produces("text/plain")]
        public IActionResult CertificateExport(string id)
        {
            try
            {
                return Ok(_certificateProvider.GetRawCertificate(id));
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (DirectoryNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
