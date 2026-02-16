using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using IptvBackend.Models;

namespace IptvBackend.Services;

public class StalkerPortalClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, PortalSession> _sessions = new();
    private readonly Dictionary<string, string> _portalEndpoints = new();
    private readonly ILogger<StalkerPortalClient> _logger;

    // Common Stalker portal endpoint paths to try
    private static readonly string[] EndpointPaths = new[]
    {
        "/portal.php",
        "/stalker_portal/server/load.php",
        "/server/load.php",
        "/c/server/load.php",
        "/stalker_portal/c/server/load.php"
    };

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
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 13).ToUpper();
    }

    private string GenerateDeviceId(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress));
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 32).ToUpper();
    }

    private string GenerateDeviceId2(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress + "device2"));
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 32).ToUpper();
    }

    private string GenerateSignature(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress + "sig"));
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 32).ToUpper();
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

        // Find the working endpoint for this portal
        var endpointPath = await DiscoverEndpointAsync(portal);
        _logger.LogInformation("Using endpoint {Endpoint} for portal {PortalId}", endpointPath, portal.Id);

        // Step 1: Handshake to get token
        var handshakeUrl = BuildUrl(portal.PortalUrl, endpointPath, new Dictionary<string, string>
        {
            ["type"] = "stb",
            ["action"] = "handshake",
            ["token"] = "",
            ["JsHttpRequest"] = "1-xml"
        });

        var handshakeHeaders = GetHandshakeHeaders(portal.MacAddress);

        _logger.LogDebug("Sending handshake request to {Url}", handshakeUrl);
        var response = await SendRequestAsync(handshakeUrl, handshakeHeaders);
        _logger.LogDebug("Handshake response: {Response}", response);

        var handshakeResult = JsonSerializer.Deserialize<StalkerHandshakeResponse>(response, _jsonOptions);

        if (handshakeResult?.Js?.Token == null)
        {
            throw new InvalidOperationException("Failed to get authentication token from handshake");
        }

        var token = handshakeResult.Js.Token;
        _logger.LogInformation("Got token from handshake: {Token}", token[..Math.Min(20, token.Length)] + "...");

        // Step 2: Get profile to complete authentication
        await GetProfileAsync(portal, token, endpointPath);

        var expiry = DateTime.UtcNow.AddMinutes(600); // 10 hours

        return new PortalSession(token, expiry, serialNumber, deviceId, deviceId2, signature);
    }

    private async Task<string> DiscoverEndpointAsync(Portal portal)
    {
        // Check if we already found the endpoint for this portal
        if (_portalEndpoints.TryGetValue(portal.Id, out var cachedEndpoint))
        {
            return cachedEndpoint;
        }

        // Try each endpoint path
        var headers = GetHandshakeHeaders(portal.MacAddress);
        var baseUri = portal.PortalUrl.TrimEnd('/');

        foreach (var path in EndpointPaths)
        {
            try
            {
                var testUrl = BuildUrl(baseUri, path, new Dictionary<string, string>
                {
                    ["type"] = "stb",
                    ["action"] = "handshake",
                    ["token"] = "",
                    ["JsHttpRequest"] = "1-xml"
                });

                _logger.LogDebug("Trying endpoint {Path} for portal {PortalId}", path, portal.Id);
                var response = await SendRequestAsync(testUrl, headers);

                // Check if we got a valid handshake response with a token
                var result = JsonSerializer.Deserialize<StalkerHandshakeResponse>(response, _jsonOptions);
                if (result?.Js?.Token != null)
                {
                    _logger.LogInformation("Found working endpoint {Path} for portal {PortalId}", path, portal.Id);
                    _portalEndpoints[portal.Id] = path;
                    return path;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Endpoint {Path} failed for portal {PortalId}: {Error}", path, portal.Id, ex.Message);
                // Continue to try next endpoint
            }
        }

        throw new InvalidOperationException($"Could not find a working endpoint for portal {portal.PortalUrl}. Tried: {string.Join(", ", EndpointPaths)}");
    }

    private Dictionary<string, string> GetHandshakeHeaders(string macAddress)
    {
        // The cookie format is critical for Stalker portals
        var cookie = $"mac={HttpUtility.UrlEncode(macAddress)}; stb_lang=en; timezone=Europe/Amsterdam";

        return new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3",
            ["X-User-Agent"] = "Model: MAG250; Link: WiFi",
            ["Accept"] = "*/*",
            ["Accept-Language"] = "en-US,en;q=0.9",
            ["Cookie"] = cookie
        };
    }

    private Dictionary<string, string> GetAuthHeaders(string macAddress, string token)
    {
        var cookie = $"mac={HttpUtility.UrlEncode(macAddress)}; stb_lang=en; timezone=Europe/Amsterdam";

        return new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3",
            ["X-User-Agent"] = "Model: MAG250; Link: WiFi",
            ["Accept"] = "*/*",
            ["Accept-Language"] = "en-US,en;q=0.9",
            ["Cookie"] = cookie,
            ["Authorization"] = $"Bearer {token}"
        };
    }

    private async Task GetProfileAsync(Portal portal, string token, string endpointPath)
    {
        // Include token in query string as some portals require it
        var profileUrl = BuildUrl(portal.PortalUrl, endpointPath, new Dictionary<string, string>
        {
            ["type"] = "stb",
            ["action"] = "get_profile",
            ["JsHttpRequest"] = "1-xml",
            ["token"] = token
        });

        var profileHeaders = GetAuthHeaders(portal.MacAddress, token);

        _logger.LogDebug("Sending get_profile request to {Url}", profileUrl);
        var response = await SendRequestAsync(profileUrl, profileHeaders);
        _logger.LogDebug("Get profile response: {Response}", response[..Math.Min(500, response.Length)]);

        // Check if response contains error
        if (response.Contains("""error""") || response.Contains("""wrong"""))
        {
            throw new InvalidOperationException($"Failed to get profile: {response}");
        }
    }

    private async Task<string> SendRequestAsync(string url, Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (headers != null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private string BuildUrl(string baseUrl, string endpointPath, Dictionary<string, string> parameters)
    {
        var baseUri = baseUrl.TrimEnd('/');
        var fullUrl = $"{baseUri}{endpointPath}";

        var uriBuilder = new UriBuilder(fullUrl);
        var query = HttpUtility.ParseQueryString(string.Empty);

        foreach (var param in parameters)
        {
            query[param.Key] = param.Value;
        }

        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    private string GetEndpointForPortal(Portal portal)
    {
        if (!_portalEndpoints.TryGetValue(portal.Id, out var endpoint))
        {
            // If we don't have a cached endpoint, we need to discover it first
            throw new InvalidOperationException("Portal endpoint not initialized. Please test the connection first.");
        }
        return endpoint;
    }

    public async Task<List<Channel>> GetChannelsAsync(Portal portal)
    {
        var session = await GetOrCreateSessionAsync(portal);
        var endpointPath = GetEndpointForPortal(portal);

        // Include token in query string as some portals require it
        var url = BuildUrl(portal.PortalUrl, endpointPath, new Dictionary<string, string>
        {
            ["type"] = "itv",
            ["action"] = "get_ordered_list",
            ["genre"] = "*",
            ["force_ch_link_check"] = "0",
            ["fav"] = "0",
            ["sortby"] = "number",
            ["p"] = "1",
            ["JsHttpRequest"] = "1-xml",
            ["token"] = session.Token
        });

        var headers = GetAuthHeaders(portal.MacAddress, session.Token);

        _logger.LogDebug("Getting channels from {Url}", url);
        var response = await SendRequestAsync(url, headers);
        _logger.LogDebug("Channels response: {Response}", response[..Math.Min(1000, response.Length)]);

        // Try to parse the response - Stalker portals can have different response structures
        try
        {
            var result = JsonSerializer.Deserialize<StalkerChannelResponse>(response, _jsonOptions);

            if (result?.Js?.Data != null)
            {
                return result.Js.Data.Select(ch => new Channel
                {
                    Id = ch.Id.ToString(),
                    Name = ch.Name,
                    Number = ch.Number,
                    Category = ch.CategoryId,
                    Logo = ch.Logo,
                    Cmd = ch.Cmd,
                    Source = "mpegts"
                }).ToList();
            }

            // Try alternative parsing if the structure is different
            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("js", out var jsElement))
            {
                List<StalkerChannel>? channels = null;

                // Try js.data first
                if (jsElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    channels = JsonSerializer.Deserialize<List<StalkerChannel>>(dataElement.GetRawText(), _jsonOptions);
                }
                // Some portals return js directly as array
                else if (jsElement.ValueKind == JsonValueKind.Array)
                {
                    channels = JsonSerializer.Deserialize<List<StalkerChannel>>(jsElement.GetRawText(), _jsonOptions);
                }

                if (channels != null)
                {
                    return channels.Select(ch => new Channel
                    {
                        Id = ch.Id.ToString(),
                        Name = ch.Name,
                        Number = ch.Number,
                        Category = ch.CategoryId,
                        Logo = ch.Logo,
                        Cmd = ch.Cmd,
                        Source = "mpegts"
                    }).ToList();
                }
            }

            _logger.LogWarning("No channels found in response or invalid response structure");
            return new List<Channel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse channels response");
            throw new InvalidOperationException($"Failed to parse channels: {ex.Message}");
        }
    }

    public async Task<List<string>> GetCategoriesAsync(Portal portal)
    {
        var session = await GetOrCreateSessionAsync(portal);
        var endpointPath = GetEndpointForPortal(portal);

        // Include token in query string as some portals require it
        var url = BuildUrl(portal.PortalUrl, endpointPath, new Dictionary<string, string>
        {
            ["type"] = "itv",
            ["action"] = "get_genres",
            ["JsHttpRequest"] = "1-xml",
            ["token"] = session.Token
        });

        var headers = GetAuthHeaders(portal.MacAddress, session.Token);

        _logger.LogDebug("Getting categories from {Url}", url);
        var response = await SendRequestAsync(url, headers);
        _logger.LogDebug("Categories response: {Response}", response[..Math.Min(500, response.Length)]);

        // Parse categories from response - Stalker returns genres in js array
        try
        {
            var doc = JsonDocument.Parse(response);
            var categories = new List<string>();

            if (doc.RootElement.TryGetProperty("js", out var jsElement) && jsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var genre in jsElement.EnumerateArray())
                {
                    if (genre.TryGetProperty("title", out var titleElement))
                    {
                        categories.Add(titleElement.GetString() ?? "");
                    }
                }
            }

            return categories.Where(c => !string.IsNullOrEmpty(c)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse categories");
            return new List<string>();
        }
    }

    public async Task<string> GetStreamUrlAsync(Portal portal, string channelId)
    {
        var session = await GetOrCreateSessionAsync(portal);
        var endpointPath = GetEndpointForPortal(portal);

        // Find the channel by ID to get the command
        var channels = await GetChannelsAsync(portal);
        var channel = channels.FirstOrDefault(c => c.Id == channelId);

        if (channel == null)
        {
            throw new InvalidOperationException($"Channel {channelId} not found");
        }

        // Include token in query string as some portals require it
        var url = BuildUrl(portal.PortalUrl, endpointPath, new Dictionary<string, string>
        {
            ["type"] = "itv",
            ["action"] = "create_link",
            ["cmd"] = channel.Cmd,
            ["series"] = "",
            ["forced_storage"] = "undefined",
            ["disable_ad"] = "0",
            ["download"] = "0",
            ["JsHttpRequest"] = "1-xml",
            ["token"] = session.Token
        });

        var headers = GetAuthHeaders(portal.MacAddress, session.Token);

        _logger.LogDebug("Creating stream link from {Url}", url);
        var response = await SendRequestAsync(url, headers);
        _logger.LogDebug("Create link response: {Response}", response);

        var result = JsonSerializer.Deserialize<StalkerLinkResponse>(response, _jsonOptions);

        if (result?.Js?.Url == null)
        {
            // Try alternative parsing
            try
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("js", out var jsElement))
                {
                    if (jsElement.TryGetProperty("url", out var urlElement))
                    {
                        return urlElement.GetString() ?? throw new InvalidOperationException("Stream URL is empty");
                    }
                    if (jsElement.TryGetProperty("cmd", out var cmdElement))
                    {
                        return cmdElement.GetString() ?? throw new InvalidOperationException("Stream cmd is empty");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse stream URL from response");
            }

            throw new InvalidOperationException("Failed to get stream URL from response");
        }

        return result.Js.Url;
    }

    public void ClearSession(string portalId)
    {
        _sessions.Remove(portalId);
        _portalEndpoints.Remove(portalId);
    }
}
