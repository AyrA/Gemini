using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Gemini.Web.Controllers
{
    [ApiController, Route("[controller]/[action]"), EnableCors("API")]
    public class ServiceController
    {
        private readonly IHostApplicationLifetime _applicationLifetime;

        public ServiceController(IHostApplicationLifetime applicationLifetime)
        {
            _applicationLifetime = applicationLifetime;
        }

        /// <summary>
        /// Shuts down the current application
        /// </summary>
        /// <returns>Always "true"</returns>
        /// <remarks>
        /// Shutdown happens approximately 1 second after the request completes
        /// </remarks>
        [HttpPost, Produces("application/json")]
        public bool Shutdown()
        {
            Task.Delay(1000).ContinueWith(t =>
            {
                _applicationLifetime.StopApplication();
            });
            return true;
        }
    }
}
