using Gemini.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Gemini.Web.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet]
        public IActionResult Index() => View();

        [HttpGet]
        public IActionResult Browse() => View();

        [HttpGet]
        public IActionResult Trust() => View();

        [HttpGet]
        public IActionResult Identity() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}