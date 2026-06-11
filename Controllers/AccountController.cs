using System.Security.Claims;
using EtisalatSaasCallback.Configuration;
using EtisalatSaasCallback.Models;
using EtisalatSaasCallback.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EtisalatSaasCallback.Controllers;

public class AccountController : Controller
{
    private readonly IUserService _userService;
    private readonly UiAuthSettings _authSettings;

    public AccountController(IUserService userService, IOptions<UiAuthSettings> authSettings)
    {
        _userService = userService;
        _authSettings = authSettings.Value;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userService.ValidateUserAsync(model.Username, model.Password);
        if (user != null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id!),
                new(ClaimTypes.Name, user.Username),
                new("DisplayName", user.DisplayName ?? user.Username),
                new(ClaimTypes.Role, user.Role.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(_authSettings.SessionTimeoutMinutes)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            await _userService.UpdateLastLoginAsync(user.Id!);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        model.ErrorMessage = "Invalid username or password";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new MyProfileViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(MyProfileViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            TempData["Error"] = "Session expired. Please login again.";
            return RedirectToAction("Login");
        }

        var user = await _userService.GetUserByIdAsync(userId);
        if (user == null)
        {
            TempData["Error"] = "User not found";
            return RedirectToAction("Login");
        }

        var validUser = await _userService.ValidateUserAsync(user.Username, model.CurrentPassword);
        if (validUser == null)
        {
            ModelState.AddModelError("CurrentPassword", "Current password is incorrect");
            return View(model);
        }

        await _userService.ChangePasswordAsync(userId, model.NewPassword);
        TempData["Success"] = "Password changed successfully";
        return RedirectToAction("Index", "Dashboard");
    }
}
