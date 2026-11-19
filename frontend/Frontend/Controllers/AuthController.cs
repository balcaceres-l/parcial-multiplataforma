using System.Security.Claims;
using Frontend.Models;
using Frontend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Frontend.Controllers;

public class AuthController : Controller
{
    private readonly IApiClientService _apiClient;

    public AuthController(IApiClientService apiClient)
    {
        _apiClient = apiClient;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var (data, result) = await _apiClient.LoginAsync(model.Username, model.Password);
        if (!result.Success || data == null)
        {
            TempData["Error"] = $"[{result.RequestId}] {result.Message}";
            return View(model);
        }

        HttpContext.Session.SetString("JWToken", data.Token);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, data.Username),
            new("NombreCompleto", data.NombreCompleto),
            new(ClaimTypes.Email, data.Email),
            new("Departamento", data.Departamento),
            new("RolPrincipal", data.Rol),
            new("JWToken", data.Token)
        };

        foreach (var r in data.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Solicitudes");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
