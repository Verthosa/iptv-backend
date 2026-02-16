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

        // Get category mapping first
        var categoryMap = await GetCategoryMapAsync(portal);

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
                var channel = ParseChannel(item, categoryMap);
                if (channel != null)
                {
                    channels.Add(channel);
                }
            }
        }

        _logger.LogInformation("Retrieved {Count} channels from portal {PortalId}", channels.Count, portal.Id);
        return channels;
    }

    private Channel? ParseChannel(JsonElement item, Dictionary<string, string> categoryMap)
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
                var categoryId = categoryProp.ValueKind == JsonValueKind.Number
                    ? categoryProp.GetInt32().ToString()
                    : categoryProp.GetString() ?? "";
                channel.CategoryId = categoryId;
                // Map category_id to category title
                if (!string.IsNullOrEmpty(categoryId) && categoryMap.TryGetValue(categoryId, out var categoryTitle))
                {
                    channel.Category = categoryTitle;
                }
                else
                {
                    channel.Category = categoryId;
                }
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

    public async Task<List<Category>> GetCategoriesAsync(Portal portal)
    {
        var session = await GetOrCreateSessionAsync(portal);
        var categories = new List<Category>();

        // Fetch all three category types
        var itvCategories = await GetItvGenresAsync(portal, session.Token);
        categories.AddRange(itvCategories);

        var vodCategories = await GetVodCategoriesAsync(portal, session.Token);
        categories.AddRange(vodCategories);

        var seriesCategories = await GetSeriesCategoriesAsync(portal, session.Token);
        categories.AddRange(seriesCategories);

        return categories;
    }

    public async Task<Dictionary<string, string>> GetCategoryMapAsync(Portal portal)
    {
        var categories = await GetCategoriesAsync(portal);
        var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            // Skip duplicates - first one wins
            if (!categoryMap.ContainsKey(category.Id))
            {
                categoryMap[category.Id] = category.Title;
            }
        }

        return categoryMap;
    }

    private async Task<List<Category>> GetItvGenresAsync(Portal portal, string token)
    {
        var url = $"{portal.PortalUrl.TrimEnd('/')}/server/load.php?type=itv&action=get_genres&JsHttpRequest=1-xml";

        using var request = CreateRequest(portal.PortalUrl, portal.MacAddress, token);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(url);

        _logger.LogDebug("Getting ITv genres from {Url}", url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("ITv genres response: {Response}", responseBody[..Math.Min(500, responseBody.Length)]);

        var categories = new List<Category>();

        using var doc = JsonDocument.Parse(responseBody);

        if (doc.RootElement.TryGetProperty("js", out var jsElement) && jsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in jsElement.EnumerateArray())
            {
                var category = new Category { Type = "itv" };

                if (item.TryGetProperty("id", out var idElement))
                {
                    category.Id = idElement.GetString() ?? "";
                }

                if (item.TryGetProperty("title", out var titleElement))
                {
                    category.Title = titleElement.GetString() ?? "";
                }

                if (!string.IsNullOrEmpty(category.Id) && !string.IsNullOrEmpty(category.Title))
                {
                    categories.Add(category);
                }
            }
        }

        return categories;
    }

    private async Task<List<Category>> GetVodCategoriesAsync(Portal portal, string token)
    {
        var url = $"{portal.PortalUrl.TrimEnd('/')}/server/load.php?type=vod&action=get_categories&JsHttpRequest=1-xml";

        using var request = CreateRequest(portal.PortalUrl, portal.MacAddress, token);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(url);

        _logger.LogDebug("Getting VOD categories from {Url}", url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("VOD categories response: {Response}", responseBody[..Math.Min(500, responseBody.Length)]);

        var categories = new List<Category>();

        using var doc = JsonDocument.Parse(responseBody);

        if (doc.RootElement.TryGetProperty("js", out var jsElement) && jsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in jsElement.EnumerateArray())
            {
                var category = new Category { Type = "vod" };

                if (item.TryGetProperty("id", out var idElement))
                {
                    category.Id = idElement.GetString() ?? "";
                }

                if (item.TryGetProperty("title", out var titleElement))
                {
                    category.Title = titleElement.GetString() ?? "";
                }

                if (!string.IsNullOrEmpty(category.Id) && !string.IsNullOrEmpty(category.Title))
                {
                    categories.Add(category);
                }
            }
        }

        return categories;
    }

    private async Task<List<Category>> GetSeriesCategoriesAsync(Portal portal, string token)
    {
        var url = $"{portal.PortalUrl.TrimEnd('/')}/server/load.php?type=series&action=get_categories&JsHttpRequest=1-xml";

        using var request = CreateRequest(portal.PortalUrl, portal.MacAddress, token);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(url);

        _logger.LogDebug("Getting Series categories from {Url}", url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Series categories response: {Response}", responseBody[..Math.Min(500, responseBody.Length)]);

        var categories = new List<Category>();

        using var doc = JsonDocument.Parse(responseBody);

        // Some servers return false or empty object when no series
        if (doc.RootElement.TryGetProperty("js", out var jsElement))
        {
            if (jsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in jsElement.EnumerateArray())
                {
                    var category = new Category { Type = "series" };

                    if (item.TryGetProperty("id", out var idElement))
                    {
                        category.Id = idElement.GetString() ?? "";
                    }

                    if (item.TryGetProperty("title", out var titleElement))
                    {
                        category.Title = titleElement.GetString() ?? "";
                    }

                    if (!string.IsNullOrEmpty(category.Id) && !string.IsNullOrEmpty(category.Title))
                    {
                        categories.Add(category);
                    }
                }
            }
            else
            {
                _logger.LogDebug("Series categories returned non-array: {ValueKind}", jsElement.ValueKind);
            }
        }

        return categories;
    }

    public async Task<string> GetStreamUrlAsync(Portal portal, string channelId)
    {
        var session = await GetOrCreateSessionAsync(portal);

        // Find the channel by ID to get the command
        var channels = await GetChannelsAsync(portal);
        var channel = channels.FirstOrDefault(c =>
            c.Id.Equals(channelId, StringComparison.OrdinalIgnoreCase));

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

        // Check for authorization errors
        if (responseBody.Contains("Authorization failed", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("Authorization filaed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Authorization failed when creating stream link");
        }

        using var doc = JsonDocument.Parse(responseBody);

        if (!doc.RootElement.TryGetProperty("js", out var jsElement))
        {
            throw new InvalidOperationException("No 'js' property in create_link response");
        }

        // Handle js being false or null
        if (jsElement.ValueKind == JsonValueKind.False || jsElement.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException("Invalid 'js' value in create_link response");
        }

        string? streamUrl = null;

        // Try to get URL from js.cmd first, then js.url
        if (jsElement.TryGetProperty("cmd", out var cmdElement) && cmdElement.ValueKind != JsonValueKind.Null)
        {
            streamUrl = cmdElement.GetString();
            _logger.LogDebug("Found stream URL in js.cmd: {Url}", streamUrl);
        }

        if (string.IsNullOrEmpty(streamUrl) && jsElement.TryGetProperty("url", out var urlElement) && urlElement.ValueKind != JsonValueKind.Null)
        {
            streamUrl = urlElement.GetString();
            _logger.LogDebug("Found stream URL in js.url: {Url}", streamUrl);
        }

        if (string.IsNullOrEmpty(streamUrl))
        {
            throw new InvalidOperationException("Failed to get stream URL from response - no cmd or url property found");
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

        // Validate URL
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"Invalid stream URL: {streamUrl}");
        }

        _logger.LogInformation("Stream URL created for channel {ChannelId}: {Url}", channelId, streamUrl);

        return streamUrl;
    }

    public void ClearSession(string portalId)
    {
        _sessions.Remove(portalId);
    }
}
