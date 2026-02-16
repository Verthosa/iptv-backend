using System.Text.Json.Serialization;

namespace IptvBackend.Models;

public class Portal
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("portalUrl")]
    public string PortalUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("macAddress")]
    public string MacAddress { get; set; } = string.Empty;
    
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [JsonPropertyName("lastConnected")]
    public DateTime? LastConnected { get; set; }
    
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;
}

public class PortalCreate
{
    public string Name { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
}

public class PortalUpdate
{
    public string? Name { get; set; }
    public string? PortalUrl { get; set; }
    public string? MacAddress { get; set; }
    public bool? IsActive { get; set; }
}
