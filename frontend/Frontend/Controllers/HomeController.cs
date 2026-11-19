using Frontend.Models;
using Frontend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Frontend.Controllers
{
    public class HomeController : Controller
    {
        private readonly IApiClientService _apiClient;

        public HomeController(IApiClientService apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task<IActionResult> Index()
        {
            var health = await _apiClient.CheckHealthAsync();
            ViewBag.ApiHealth = health;
            ViewBag.ApiBaseUrl = _apiClient.BaseUrl;
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
