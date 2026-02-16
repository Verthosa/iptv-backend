using System.Text.Json;
using IptvBackend.Models;

namespace IptvBackend.Services;

public class PortalStore
{
    private readonly string _dataPath;
    private readonly string _portalsFile;
    private readonly JsonSerializerOptions _jsonOptions;
    private List<Portal> _portals = new();
    private readonly object _lock = new();
    private bool _loaded = false;

    public PortalStore(IWebHostEnvironment env)
    {
        _dataPath = Path.Combine(env.ContentRootPath, "data");
        _portalsFile = Path.Combine(_dataPath, "portals.json");
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

            if (File.Exists(_portalsFile))
            {
                try
                {
                    var json = File.ReadAllText(_portalsFile);
                    _portals = JsonSerializer.Deserialize<List<Portal>>(json, _jsonOptions) ?? new();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading portals: {ex.Message}");
                    _portals = new();
                }
            }
            else
            {
                _portals = new();
                SaveSync();
            }
            _loaded = true;
        }
    }

    private void SaveSync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_portals, _jsonOptions);
            File.WriteAllText(_portalsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving portals: {ex.Message}");
        }
    }

    public async Task<List<Portal>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return _portals.ToList();
    }

    public async Task<Portal?> GetByIdAsync(string id)
    {
        await EnsureLoadedAsync();
        return _portals.FirstOrDefault(p => p.Id == id);
    }

    public async Task<Portal> AddAsync(Portal portal)
    {
        await EnsureLoadedAsync();
        
        lock (_lock)
        {
            portal.Id = Guid.NewGuid().ToString();
            portal.CreatedAt = DateTime.UtcNow;
            _portals.Add(portal);
            SaveSync();
        }
        
        return portal;
    }

    public async Task<Portal?> UpdateAsync(string id, PortalUpdate update)
    {
        await EnsureLoadedAsync();
        
        lock (_lock)
        {
            var portal = _portals.FirstOrDefault(p => p.Id == id);
            if (portal == null) return null;

            if (update.Name != null) portal.Name = update.Name;
            if (update.PortalUrl != null) portal.PortalUrl = update.PortalUrl;
            if (update.MacAddress != null) portal.MacAddress = update.MacAddress;
            if (update.IsActive.HasValue) portal.IsActive = update.IsActive.Value;

            SaveSync();
            return portal;
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await EnsureLoadedAsync();
        
        lock (_lock)
        {
            var portal = _portals.FirstOrDefault(p => p.Id == id);
            if (portal == null) return false;
            
            _portals.Remove(portal);
            SaveSync();
            return true;
        }
    }

    public async Task UpdateLastConnectedAsync(string id)
    {
        await EnsureLoadedAsync();
        
        lock (_lock)
        {
            var portal = _portals.FirstOrDefault(p => p.Id == id);
            if (portal != null)
            {
                portal.LastConnected = DateTime.UtcNow;
                SaveSync();
            }
        }
    }
}
