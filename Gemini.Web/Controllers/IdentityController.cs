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
        public CertificateInfoViewModel[] CertificateList()
        {
            return _certificateProvider
                .GetCertificateNames()
                .Select(m => new CertificateInfoViewModel(_certificateProvider.GetPublicCertificate(m)))
                .ToArray();
        }

        /// <summary>
        /// Changes the password of a certificate.
        /// </summary>
        /// <param name="id">Certificate id</param>
        /// <param name="currentPassword">Current password</param>
        /// <param name="newPassword">New password</param>
        /// <returns>Certificate info</returns>
        /// <remarks>
        /// This function is also used to add or remove the password.
        /// Encryption is added by sending an empty <paramref name="currentPassword"/> and a non-empty
        /// <paramref name="newPassword"/>.
        /// Likewise, encryption is removed by setting <paramref name="currentPassword"/> and leaving
        /// <paramref name="newPassword"/> empty.
        /// </remarks>
        [HttpPost, Produces("application/json", Type = typeof(CertificateInfoViewModel))]
        public IActionResult ChangePassword(string id, [FromForm] string? currentPassword, [FromForm] string? newPassword)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest($"'{nameof(id)}' cannot be null or empty.");
            }
            if (string.IsNullOrEmpty(currentPassword) && string.IsNullOrEmpty(newPassword))
            {
                return BadRequest("Old and new password are identical");
            }
            currentPassword ??= string.Empty;
            newPassword ??= string.Empty;

            if (currentPassword == newPassword)
            {
                return BadRequest("Old and new password are identical");
            }
            return Json(new CertificateInfoViewModel(_certificateProvider.UpdatePassword(id, currentPassword, newPassword)));
        }

        /// <summary>
        /// Gets the information of a single certificate
        /// </summary>
        /// <param name="id">Certificate id</param>
        /// <returns>Certificate</returns>
        /// <remarks>
        /// To get the certificate data itself, use <see cref="CertificateExport(string)"/> function instead
        /// </remarks>
        [HttpGet, ActionName("Certificate"), Produces("application/json", Type = typeof(CertificateInfoViewModel))]
        public IActionResult CertificateGet(string id)
        {
            try
            {
                return Json(new CertificateInfoViewModel(_certificateProvider.GetPublicCertificate(id)));
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
        /// <param name="password">
        /// Optional private key password.
        /// If not specified, the key is stored unencrypted (not recommended)
        /// </param>
        /// <param name="expiration">Expiration date</param>
        /// <returns>Created certificate resource</returns>
        [HttpPost]
        [ActionName("Certificate")]
        [Produces("application/json", Type = typeof(CertificateInfoViewModel))]
        [Route("/[controller]/[action]")]
        public IActionResult CertificatePost([FromForm] string displayName, [FromForm] string? password, [FromForm] DateTime expiration)
        {
            var exp = expiration.ToLocalTime().ToUniversalTime().Date;
            try
            {
                return Json(new CertificateInfoViewModel(_certificateProvider.CreateNew(displayName, password, exp)));
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
        /// <param name="password">Password to decrypt <paramref name="certificate"/></param>
        /// <returns>Imported certificate resource</returns>
        /// <remarks>
        /// The certificate can either be a PKCS12 file (these usually have PFX extension),
        /// or a PEM formatted file with both certificate and private key inside
        /// </remarks>
        [HttpPut]
        [ActionName("Certificate")]
        [Produces("application/json", Type = typeof(CertificateInfoViewModel))]
        [Consumes("multipart/form-data")]
        [Route("/[controller]/[action]")]
        public IActionResult CertificatePut(IFormFile certificate, [FromForm] string? password)
        {
            if (certificate is null)
            {
                return BadRequest("No file provided");
            }
            //Full  certificates with private key are usually just a few KB
            if (certificate.Length > 1024 * 1024)
            {
                return BadRequest("Certificate too large.");
            }

            try
            {
                using var ms = new MemoryStream();
                certificate.CopyToAsync(ms);
                return Json(new CertificateInfoViewModel(_certificateProvider.Import(ms.ToArray(), password)));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Deletes a certificate
        /// </summary>
        /// <param name="id">Certificate id</param>
        /// <returns>true, if deleted or id doesn't exists, false otherwise</returns>
        /// <remarks>
        /// Deleting the certificate will irrevocably remove it.
        /// You will not be able to recreate the certificate, even if you supply the exact same values again.
        /// If you may need the certificate at a later point in time, export it before deleting it.
        /// </remarks>
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
        /// A server that identifies the client with the key will continue to correctly identify this new certificate.
        /// A server that uses the thumbprint (which is incorrect to do so)
        /// will no longer recognize the certificate.
        /// </remarks>
        [HttpPatch, ActionName("Certificate"), Produces("application/json", Type = typeof(CertificateInfoViewModel))]
        public IActionResult CertificatePatch(
            string id, [FromForm] string? password, [FromForm] string displayName, [FromForm] DateTime expiration)
        {
            var exp = expiration.ToLocalTime().ToUniversalTime().Date;
            try
            {
                return Json(new CertificateInfoViewModel(_certificateProvider.Update(id, displayName, password, exp)));
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
        /// <remarks>
        /// Whether the export will be encrypted or not
        /// depends on whether the certificate is currently encrypted or not.
        /// Use <see cref="ChangePassword"/> to change encryption.
        /// </remarks>
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
