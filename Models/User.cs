using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace EtisalatSaasCallback.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("serviceme_username")]
    public string Username { get; set; } = null!;

    [BsonElement("serviceme_passwordHash")]
    public string PasswordHash { get; set; } = null!;

    [BsonElement("serviceme_email")]
    public string? Email { get; set; }

    [BsonElement("serviceme_displayName")]
    public string? DisplayName { get; set; }

    [BsonElement("serviceme_role")]
    public UserRole Role { get; set; } = UserRole.User;

    [BsonElement("serviceme_isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("serviceme_createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("serviceme_lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }

    [BsonElement("serviceme_createdBy")]
    public string? CreatedBy { get; set; }
}

public enum UserRole
{
    User = 0,
    Admin = 1
}

public class CreateUserViewModel
{
    [Required(ErrorMessage = "Username is required")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username can only contain letters, numbers, and underscores")]
    public string Username { get; set; } = null!;

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
    public string Password { get; set; } = null!;

    [Required(ErrorMessage = "Confirm password is required")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = null!;

    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string? Email { get; set; }

    [StringLength(100)]
    public string? DisplayName { get; set; }

    public UserRole Role { get; set; } = UserRole.User;
}

public class EditUserViewModel
{
    public string Id { get; set; } = null!;

    [Required(ErrorMessage = "Username is required")]
    public string Username { get; set; } = null!;

    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string? Email { get; set; }

    [StringLength(100)]
    public string? DisplayName { get; set; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; }
}

public class ChangePasswordViewModel
{
    public string UserId { get; set; } = null!;
    public string Username { get; set; } = null!;

    [Required(ErrorMessage = "New password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
    public string NewPassword { get; set; } = null!;

    [Required(ErrorMessage = "Confirm password is required")]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = null!;
}

public class MyProfileViewModel
{
    [Required(ErrorMessage = "Current password is required")]
    public string CurrentPassword { get; set; } = null!;

    [Required(ErrorMessage = "New password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
    public string NewPassword { get; set; } = null!;

    [Required(ErrorMessage = "Confirm password is required")]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = null!;
}
