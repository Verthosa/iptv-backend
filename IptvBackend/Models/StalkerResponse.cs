using System.Text.Json.Serialization;

namespace IptvBackend.Models;

public class StalkerHandshakeResponse
{
    [JsonPropertyName("js")]
    public StalkerJsData? Js { get; set; }
}

public class StalkerJsData
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("not_valid_token")]
    public int NotValidToken { get; set; }
}

public class StalkerChannel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = string.Empty;
}

public class StalkerChannelResponse
{
    [JsonPropertyName("js")]
    public StalkerChannelData? Js { get; set; }
}

public class StalkerChannelData
{
    [JsonPropertyName("data")]
    public List<StalkerChannel>? Data { get; set; }
}

public class StalkerLinkResponse
{
    [JsonPropertyName("js")]
    public StalkerLinkData? Js { get; set; }
}

public class StalkerLinkData
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("cmd")]
    public string? Cmd { get; set; }
}

public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("data")]
    public T? Data { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
