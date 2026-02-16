using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IptvBackend.Models;

namespace IptvBackend.Services;

public class UserStore
{
    private readonly string _dataPath;
    private readonly string _usersFile;
    private readonly JsonSerializerOptions _jsonOptions;
    private List<User> _users = new();
    private readonly object _lock = new();
    private bool _loaded = false;

    public UserStore(IWebHostEnvironment env)
    {
        _dataPath = Path.Combine(env.ContentRootPath, "data");
        _usersFile = Path.Combine(_dataPath, "users.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        EnsureDataDirectory();
    }

    private void EnsureDataDirectory()
    {
        if (!Directory.Exists(_dataPath))
        {
            Directory.CreateDirectory(_dataPath);
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        lock (_lock)
        {
            if (_loaded) return;

            if (File.Exists(_usersFile))
            {
                try
                {
                    var json = File.ReadAllText(_usersFile);
                    _users = JsonSerializer.Deserialize<List<User>>(json, _jsonOptions) ?? new();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading users: {ex.Message}");
                    _users = new();
                }
            }
            else
            {
                _users = new();
                SaveSync();
            }
            _loaded = true;
        }
    }

    private void SaveSync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_users, _jsonOptions);
            File.WriteAllText(_usersFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving users: {ex.Message}");
        }
    }

    public async Task<List<User>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return _users.ToList();
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        await EnsureLoadedAsync();
        return _users.FirstOrDefault(u => u.Id == id);
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        await EnsureLoadedAsync();
        return _users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        await EnsureLoadedAsync();
        return _users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<User> CreateAsync(string username, string password, string email)
    {
        await EnsureLoadedAsync();

        lock (_lock)
        {
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                PasswordHash = HashPassword(password),
                Email = email,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _users.Add(user);
            SaveSync();

            return user;
        }
    }

    public async Task<User?> ValidateCredentialsAsync(string username, string password)
    {
        await EnsureLoadedAsync();

        var user = await GetByUsernameAsync(username);
        if (user == null || !user.IsActive)
            return null;

        if (!VerifyPassword(password, user.PasswordHash))
            return null;

        return user;
    }

    public async Task<bool> ChangePasswordAsync(string userId, string newPassword)
    {
        await EnsureLoadedAsync();

        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return false;

            user.PasswordHash = HashPassword(newPassword);
            SaveSync();
            return true;
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await EnsureLoadedAsync();

        lock (_lock)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);
            if (user == null) return false;

            _users.Remove(user);
            SaveSync();
            return true;
        }
    }

    public async Task<int> GetUserCountAsync()
    {
        await EnsureLoadedAsync();
        return _users.Count;
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var saltedPassword = password + "IPTV_SALT_V1"; // Simple salt - in production use a unique salt per user
        var bytes = Encoding.UTF8.GetBytes(saltedPassword);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }
}
