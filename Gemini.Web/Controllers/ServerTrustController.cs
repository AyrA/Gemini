using Gemini.Web.Models;
using Gemini.Web.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Gemini.Web.Controllers
{
    /// <summary>
    /// Provides access to the server trust list
    /// </summary>
    [ApiController, Route("[controller]/[action]/{host}"), EnableCors("API")]
    public class ServerTrustController(ServerIdentityService serverIdentityService) : Controller
    {
        /// <summary>
        /// Gets all trusted keys of all known hosts
        /// </summary>
        /// <returns>Complete trust list</returns>
        [HttpGet, Route("/[controller]/[action]")]
        public ServerIdentityModel[] TrustList()
        {
            return [.. serverIdentityService.GetAll().OrderBy(m => m.Host)];
        }

        /// <summary>
        /// Gets all trusted keys of a single host
        /// </summary>
        /// <param name="host">host</param>
        /// <returns>Trusted keys</returns>
        [HttpGet, ActionName("Trust"), Produces("application/json", Type = typeof(ServerIdentityKeyModel[]))]
        public IActionResult TrustGet(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return BadRequest();
            }
            var entries = serverIdentityService
                .GetAll()
                .Where(m => m.Host.Equals(host, StringComparison.InvariantCultureIgnoreCase))
                .SelectMany(m => m.PublicKeys)
                .ToArray();
            return Json(entries);
        }

        /// <summary>
        /// Trusts a new key for a host
        /// </summary>
        /// <param name="host">Host</param>
        /// <param name="base64Cert">Raw certificate data as base64 encoded string</param>
        /// <returns>Created key entry</returns>
        [HttpPut, ActionName("Trust"), Produces("application/json", Type = typeof(ServerIdentityKeyModel))]
        public IActionResult TrustPut(string host, [FromForm] string base64Cert)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(base64Cert))
            {
                return BadRequest("Missing or empty argument");
            }
            var cert = B64(base64Cert);
            if (cert == null)
            {
                return BadRequest("Invalid key");
            }
            try
            {
                return Json(serverIdentityService.AddServerTrust(host, cert));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Deletes a trusted key from a host
        /// </summary>
        /// <param name="host">host</param>
        /// <param name="id">key id</param>
        /// <returns>true, if deleted, false otherwise</returns>
        [HttpDelete, ActionName("Trust"), Produces("application/json", Type = typeof(bool))]
        public IActionResult TrustDelete(string host, [FromForm] string id)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(id))
            {
                return BadRequest("Missing or empty argument");
            }
            return Json(serverIdentityService.RemoveKey(host, id));
        }

        private static byte[]? B64(string data)
        {
            try
            {
                return Convert.FromBase64String(data);
            }
            catch
            {
                return null;
            }
        }
    }
}
