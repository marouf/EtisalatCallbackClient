using EtisalatSaasCallback.Configuration;
using EtisalatSaasCallback.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Security.Cryptography;

namespace EtisalatSaasCallback.Services;

public interface IUserService
{
    Task<User?> ValidateUserAsync(string username, string password);
    Task<User?> GetUserByIdAsync(string id);
    Task<User?> GetUserByUsernameAsync(string username);
    Task<List<User>> GetAllUsersAsync();
    Task<User> CreateUserAsync(User user, string password);
    Task<bool> UpdateUserAsync(User user);
    Task<bool> ChangePasswordAsync(string userId, string newPassword);
    Task<bool> DeleteUserAsync(string userId);
    Task UpdateLastLoginAsync(string userId);
    Task EnsureDefaultAdminAsync();
}

public class UserService : IUserService
{
    private readonly IMongoCollection<User> _users;
    private readonly ILogger<UserService> _logger;
    private readonly UiAuthSettings _authSettings;

    public UserService(
        IOptions<MongoDbSettings> mongoSettings,
        IOptions<UiAuthSettings> authSettings,
        ILogger<UserService> logger)
    {
        var client = new MongoClient(mongoSettings.Value.ConnectionString);
        var database = client.GetDatabase(mongoSettings.Value.DatabaseName);
        _users = database.GetCollection<User>("serviceme_users");
        _logger = logger;
        _authSettings = authSettings.Value;

        CreateIndexes();
    }

    private void CreateIndexes()
    {
        var indexKeys = Builders<User>.IndexKeys.Ascending(u => u.Username);
        var indexOptions = new CreateIndexOptions { Unique = true };
        _users.Indexes.CreateOne(new CreateIndexModel<User>(indexKeys, indexOptions));
    }

    public async Task EnsureDefaultAdminAsync()
    {
        var adminExists = await _users.Find(u => u.Role == UserRole.Admin).AnyAsync();
        if (!adminExists)
        {
            var defaultAdmin = new User
            {
                Username = _authSettings.Username,
                DisplayName = "Administrator",
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            defaultAdmin.PasswordHash = HashPassword(_authSettings.Password);
            await _users.InsertOneAsync(defaultAdmin);
            _logger.LogInformation("Default admin user created: {Username}", _authSettings.Username);
        }
    }

    public async Task<User?> ValidateUserAsync(string username, string password)
    {
        var user = await _users.Find(u => u.Username == username && u.IsActive).FirstOrDefaultAsync();
        if (user == null)
            return null;

        if (!VerifyPassword(password, user.PasswordHash))
            return null;

        return user;
    }

    public async Task<User?> GetUserByIdAsync(string id)
    {
        return await _users.Find(u => u.Id == id).FirstOrDefaultAsync();
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _users.Find(u => u.Username == username).FirstOrDefaultAsync();
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        return await _users.Find(_ => true).SortByDescending(u => u.CreatedAt).ToListAsync();
    }

    public async Task<User> CreateUserAsync(User user, string password)
    {
        user.PasswordHash = HashPassword(password);
        user.CreatedAt = DateTime.UtcNow;
        await _users.InsertOneAsync(user);
        _logger.LogInformation("User created: {Username} by {CreatedBy}", user.Username, user.CreatedBy);
        return user;
    }

    public async Task<bool> UpdateUserAsync(User user)
    {
        var updateDef = Builders<User>.Update
            .Set(u => u.Email, user.Email)
            .Set(u => u.DisplayName, user.DisplayName)
            .Set(u => u.Role, user.Role)
            .Set(u => u.IsActive, user.IsActive);

        var result = await _users.UpdateOneAsync(u => u.Id == user.Id, updateDef);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> ChangePasswordAsync(string userId, string newPassword)
    {
        var hash = HashPassword(newPassword);
        var result = await _users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.Set(u => u.PasswordHash, hash));
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        var result = await _users.DeleteOneAsync(u => u.Id == userId);
        return result.DeletedCount > 0;
    }

    public async Task UpdateLastLoginAsync(string userId)
    {
        await _users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<User>.Update.Set(u => u.LastLoginAt, DateTime.UtcNow));
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var salt = GenerateSalt();
        var saltedPassword = salt + password;
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(saltedPassword));
        return salt + ":" + Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2)
            return false;

        var salt = parts[0];
        var hash = parts[1];

        using var sha256 = SHA256.Create();
        var saltedPassword = salt + password;
        var computedHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(saltedPassword)));

        return hash == computedHash;
    }

    private static string GenerateSalt()
    {
        var saltBytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(saltBytes);
        return Convert.ToBase64String(saltBytes);
    }
}
