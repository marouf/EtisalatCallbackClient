using System.Security.Claims;
using EtisalatSaasCallback.Models;
using EtisalatSaasCallback.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EtisalatSaasCallback.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController : Controller
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userService.GetAllUsersAsync();
        return View(users);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existingUser = await _userService.GetUserByUsernameAsync(model.Username);
        if (existingUser != null)
        {
            ModelState.AddModelError("Username", "Username already exists");
            return View(model);
        }

        var user = new User
        {
            Username = model.Username,
            Email = model.Email,
            DisplayName = model.DisplayName ?? model.Username,
            Role = model.Role,
            IsActive = true,
            CreatedBy = User.Identity?.Name
        };

        await _userService.CreateUserAsync(user, model.Password);

        TempData["Success"] = $"User '{model.Username}' created successfully";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null)
        {
            TempData["Error"] = "User not found";
            return RedirectToAction("Index");
        }

        var model = new EditUserViewModel
        {
            Id = user.Id!,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userService.GetUserByIdAsync(model.Id);
        if (user == null)
        {
            TempData["Error"] = "User not found";
            return RedirectToAction("Index");
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id == currentUserId && !model.IsActive)
        {
            ModelState.AddModelError("IsActive", "You cannot deactivate your own account");
            return View(model);
        }

        if (user.Id == currentUserId && model.Role != UserRole.Admin)
        {
            ModelState.AddModelError("Role", "You cannot remove admin role from yourself");
            return View(model);
        }

        user.Email = model.Email;
        user.DisplayName = model.DisplayName;
        user.Role = model.Role;
        user.IsActive = model.IsActive;

        await _userService.UpdateUserAsync(user);

        TempData["Success"] = $"User '{user.Username}' updated successfully";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> ChangePassword(string id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null)
        {
            TempData["Error"] = "User not found";
            return RedirectToAction("Index");
        }

        return View(new ChangePasswordViewModel
        {
            UserId = user.Id!,
            Username = user.Username
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userService.GetUserByIdAsync(model.UserId);
        if (user == null)
        {
            TempData["Error"] = "User not found";
            return RedirectToAction("Index");
        }

        await _userService.ChangePasswordAsync(model.UserId, model.NewPassword);

        _logger.LogInformation("Password changed for user {Username} by {Admin}",
            user.Username, User.Identity?.Name);

        TempData["Success"] = $"Password changed for user '{user.Username}'";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null)
        {
            TempData["Error"] = "User not found";
            return RedirectToAction("Index");
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id == currentUserId)
        {
            TempData["Error"] = "You cannot delete your own account";
            return RedirectToAction("Index");
        }

        await _userService.DeleteUserAsync(id);

        _logger.LogInformation("User {Username} deleted by {Admin}",
            user.Username, User.Identity?.Name);

        TempData["Success"] = $"User '{user.Username}' deleted successfully";
        return RedirectToAction("Index");
    }
}
