using System.Text.Json.Serialization;

namespace IptvBackend.Models;

public class Channel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("categoryId")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty; // mpegts/hls/http
}

public class Category
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // itv, vod, series
}

public class ChannelResponse
{
    public List<Channel> Channels { get; set; } = new();
}

public class CategoriesResponse
{
    public List<Category> Categories { get; set; } = new();
}

public class StreamRequest
{
    public string PortalId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
}
