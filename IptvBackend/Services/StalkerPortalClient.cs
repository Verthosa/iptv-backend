using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IptvBackend.Models;

namespace IptvBackend.Services;

public class StalkerPortalClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, PortalSession> _sessions = new();
    private readonly ILogger<StalkerPortalClient> _logger;

    public StalkerPortalClient(HttpClient httpClient, ILogger<StalkerPortalClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    private record PortalSession(
        string Token,
        DateTime Expiry,
        string SerialNumber,
        string DeviceId,
        string DeviceId2,
        string Signature
    );

    private string GenerateSerial(string macAddress)
    {
        var mac = macAddress.Replace(":", "").ToUpper();
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(mac));
        var hexString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return hexString.Substring(0, 13).ToUpperInvariant();
    }

    private string GenerateDeviceId(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress));
        return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
    }

    private string GenerateDeviceId2(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress + "device2"));
        return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
    }

    private string GenerateSignature(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress + "sig"));
        return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
    }

    private string BuildCookieString(string macAddress, string? token = null)
    {
        var cookies = new List<string>
        {
            $"mac={macAddress}",
            "stb_lang=en",
            "timezone=Europe/London"
        };

        if (!string.IsNullOrEmpty(token))
        {
            cookies.Add($"token={token}");
        }

        return string.Join("; ", cookies);
    }

    private HttpRequestMessage CreateRequest(string baseUrl, string macAddress, string? token = null)
    {
        var baseUri = new Uri(baseUrl.TrimEnd('/'));
        var request = new HttpRequestMessage();

        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3");
        request.Headers.TryAddWithoutValidation("Referer", $"{baseUrl.TrimEnd('/')}/c/index.html");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        request.Headers.TryAddWithoutValidation("X-User-Agent", "Model: MAG250; Link: WiFi");
        request.Headers.TryAddWithoutValidation("Host", baseUri.Host);
        request.Headers.TryAddWithoutValidation("Connection", "Close");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        request.Headers.TryAddWithoutValidation("Cookie", BuildCookieString(macAddress, token));

        return request;
    }

    private async Task<PortalSession> GetOrCreateSessionAsync(Portal portal)
    {
        if (_sessions.TryGetValue(portal.Id, out var existingSession) &&
            existingSession.Expiry > DateTime.UtcNow.AddMinutes(5))
        {
            _logger.LogDebug("Using cached session for portal {PortalId}", portal.Id);
            return existingSession;
        }

        _logger.LogInformation("Creating new session for portal {PortalId}", portal.Id);
        var session = await CreateSessionAsync(portal);
        _sessions[portal.Id] = session;
        return session;
    }

    private async Task<PortalSession> CreateSessionAsync(Portal portal)
    {
        var serialNumber = GenerateSerial(portal.MacAddress);
        var deviceId = GenerateDeviceId(portal.MacAddress);
        var deviceId2 = GenerateDeviceId2(portal.MacAddress);
        var signature = GenerateSignature(portal.MacAddress);

        // Step 1: Handshake to get token
        var handshakeUrl = $"{portal.PortalUrl.TrimEnd('/')}/server/load.php?type=stb&action=handshake&JsHttpRequest=1-xml";

        using var handshakeRequest = CreateRequest(portal.PortalUrl, portal.MacAddress);
        handshakeRequest.Method = HttpMethod.Get;
        handshakeRequest.RequestUri = new Uri(handshakeUrl);

        _logger.LogDebug("Sending handshake request to {Url}", handshakeUrl);
        var response = await _httpClient.SendAsync(handshakeRequest);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Handshake response: {Response}", responseBody);

        using var doc = JsonDocument.Parse(responseBody);
        string? token = null;

        if (doc.RootElement.TryGetProperty("js", out var jsElement))
        {
            if (jsElement.TryGetProperty("token", out var tokenElement))
            {
                token = tokenElement.GetString();
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Failed to get authentication token from handshake");
        }

        _logger.LogInformation("Got token from handshake: {Token}", token[..Math.Min(20, token.Length)] + "...");

        // Step 2: Get profile to complete authentication
        await GetProfileAsync(portal, token);

        var expiry = DateTime.UtcNow.AddMinutes(600); // 10 hours

        return new PortalSession(token, expiry, serialNumber, deviceId, deviceId2, signature);
    }

    private async Task GetProfileAsync(Portal portal, string token)
    {
        var profileUrl = $"{portal.PortalUrl.TrimEnd('/')}/server/load.php?type=stb&action=get_profile&JsHttpRequest=1-xml";

        using var profileRequest = CreateRequest(portal.PortalUrl, portal.MacAddress, token);
        profileRequest.Method = HttpMethod.Get;
        profileRequest.RequestUri = new Uri(profileUrl);

        _logger.LogDebug("Sending get_profile request to {Url}", profileUrl);
        var response = await _httpClient.SendAsync(profileRequest);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Get profile response: {Response}", responseBody[..Math.Min(500, responseBody.Length)]);

        if (responseBody.Contains("Authorization failed", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("Authorization filaed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Authorization failed. Response: {responseBody}");
        }
    }

    public async Task<List<Channel>> GetChannelsAsync(Portal portal)
    {
        var session = await GetOrCreateSessionAsync(portal);

        var url = $"{portal.PortalUrl.TrimEnd('/')}/server/load.php?type=itv&action=get_ordered_list&genre=*&force_ch_link_check=0&fav=0&sortby=number&p=1&JsHttpRequest=1-xml";

        using var request = CreateRequest(portal.PortalUrl, portal.MacAddress, session.Token);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(url);

        _logger.LogDebug("Getting channels from {Url}", url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Channels response: {Response}", responseBody[..Math.Min(1000, responseBody.Length)]);

        if (responseBody.Contains("Authorization failed", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("Authorization filaed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Authorization failed when fetching channels");
        }

        var channels = new List<Channel>();

        using var doc = JsonDocument.Parse(responseBody);

        if (!doc.RootElement.TryGetProperty("js", out var jsElement))
        {
            _logger.LogWarning("No 'js' property found in response");
            return channels;
        }

        // Handle js being false or empty
        if (jsElement.ValueKind == JsonValueKind.False ||
            (jsElement.ValueKind == JsonValueKind.Array && jsElement.GetArrayLength() == 0))
        {
            return channels;
        }

        // Get data array from js.data
        if (jsElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataElement.EnumerateArray())
            {
                var channel = ParseChannel(item);
                if (channel != null)
                {
                    channels.Add(channel);
                }
            }
        }

        _logger.LogInformation("Retrieved {Count} channels from portal {PortalId}", channels.Count, portal.Id);
        return channels;
    }

    private Channel? ParseChannel(JsonElement item)
    {
        try
        {
            var channel = new Channel
            {
                Source = "mpegts"
            };

            if (item.TryGetProperty("id", out var idProp))
            {
                channel.Id = idProp.ValueKind == JsonValueKind.Number
                    ? idProp.GetInt32().ToString()
                    : idProp.GetString() ?? "";
            }

            if (item.TryGetProperty("name", out var nameProp))
            {
                channel.Name = nameProp.GetString() ?? "";
            }

            if (item.TryGetProperty("number", out var numberProp))
            {
                channel.Number = numberProp.ValueKind == JsonValueKind.Number
                    ? numberProp.GetInt32()
                    : 0;
            }

            if (item.TryGetProperty("category_id", out var categoryProp))
            {
                channel.Category = categoryProp.GetString() ?? "";
            }

            if (item.TryGetProperty("logo", out var logoProp) && logoProp.ValueKind != JsonValueKind.Null)
            {
                channel.Logo = logoProp.GetString();
            }

            if (item.TryGetProperty("cmd", out var cmdProp))
            {
                channel.Cmd = cmdProp.GetString() ?? "";
            }

            return channel;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse channel item");
            return null;
        }
    }

    public async Task<List<string>> GetCategoriesAsync(Portal portal)
    {
        var session = await GetOrCreateSessionAsync(portal);

        var url = $"{portal.PortalUrl.TrimEnd('/')}/server/load.php?type=itv&action=get_genres&JsHttpRequest=1-xml";

        using var request = CreateRequest(portal.PortalUrl, portal.MacAddress, session.Token);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(url);

        _logger.LogDebug("Getting categories from {Url}", url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Categories response: {Response}", responseBody[..Math.Min(500, responseBody.Length)]);

        var categories = new List<string>();

        using var doc = JsonDocument.Parse(responseBody);

        if (doc.RootElement.TryGetProperty("js", out var jsElement) && jsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in jsElement.EnumerateArray())
            {
                if (item.TryGetProperty("title", out var titleElement))
                {
                    var title = titleElement.GetString();
                    if (!string.IsNullOrEmpty(title))
                    {
                        categories.Add(title);
                    }
                }
            }
        }

        return categories;
    }

    public async Task<string> GetStreamUrlAsync(Portal portal, string channelId)
    {
        var session = await GetOrCreateSessionAsync(portal);

        // Find the channel by ID to get the command
        var channels = await GetChannelsAsync(portal);
        var channel = channels.FirstOrDefault(c => c.Id == channelId);

        if (channel == null)
        {
            throw new InvalidOperationException($"Channel {channelId} not found");
        }

        // Clean the cmd if needed
        var cmd = channel.Cmd;

        // Handle ffrt http:/// format
        if (cmd.Contains("ffrt") && cmd.Contains("http:///"))
        {
            var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (Uri.TryCreate(parts[0], UriKind.Absolute, out var uriHost))
                {
                    string path = parts[1].Replace("http:///", "").Replace("http://", "");
                    cmd = $"{uriHost.Scheme}://{uriHost.Authority}/{path.TrimStart('/')}";
                }
            }
        }
        else if (cmd.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase))
        {
            cmd = cmd.Substring(7).Trim();
        }

        var encodedCmd = Uri.EscapeDataString(cmd);
        var url = $"{portal.PortalUrl.TrimEnd('/')}/server/load.php?type=itv&action=create_link&cmd={encodedCmd}&series=&forced_storage=undefined&disable_ad=0&download=0&JsHttpRequest=1-xml";

        using var request = CreateRequest(portal.PortalUrl, portal.MacAddress, session.Token);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(url);

        _logger.LogDebug("Creating stream link from {Url}", url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Create link response: {Response}", responseBody);

        using var doc = JsonDocument.Parse(responseBody);

        if (!doc.RootElement.TryGetProperty("js", out var jsElement))
        {
            throw new InvalidOperationException("No 'js' property in create_link response");
        }

        string? streamUrl = null;

        // Try to get URL from js.cmd or js.url
        if (jsElement.TryGetProperty("cmd", out var cmdElement))
        {
            streamUrl = cmdElement.GetString();
        }
        else if (jsElement.TryGetProperty("url", out var urlElement))
        {
            streamUrl = urlElement.GetString();
        }

        if (string.IsNullOrEmpty(streamUrl))
        {
            throw new InvalidOperationException("Failed to get stream URL from response");
        }

        // Clean the stream URL
        // Remove ffmpeg prefix
        if (streamUrl.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase))
        {
            streamUrl = streamUrl.Substring(7).Trim();
        }

        // Fix localhost URLs
        if (streamUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
            streamUrl.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(streamUrl, UriKind.Absolute, out var localUri))
            {
                var baseUri = new Uri(portal.PortalUrl.TrimEnd('/'));
                streamUrl = $"{baseUri.Scheme}://{baseUri.Host}/{localUri.PathAndQuery.TrimStart('/')}";
            }
        }

        return streamUrl;
    }

    public void ClearSession(string portalId)
    {
        _sessions.Remove(portalId);
    }
}
