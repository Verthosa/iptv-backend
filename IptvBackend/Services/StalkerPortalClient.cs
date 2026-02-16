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
        return Convert.ToHexString(hash).Substring(0, 13).ToUpper();
    }

    private string GenerateDeviceId(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress));
        return Convert.ToHexString(hash).Substring(0, 32).ToUpper();
    }

    private string GenerateDeviceId2(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress + "device2"));
        return Convert.ToHexString(hash).Substring(0, 32).ToUpper();
    }

    private string GenerateSignature(string macAddress)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress + "sig"));
        return Convert.ToHexString(hash).Substring(0, 32).ToUpper();
    }

    private Dictionary<string, string> GetBaseHeaders(string macAddress)
    {
        return new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG200 stbapp ver: 2 rev: 250 Safari/533.3",
            ["X-User-Agent"] = "Model: MAG250; Link: WiFi",
            ["Accept"] = "*/*",
            ["Accept-Language"] = "en-US,en;q=0.9",
            ["Cookie"] = $"mac={HttpUtility.UrlEncode(macAddress)}; stb_lang=en; timezone=Europe/Amsterdam"
        };
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
        var handshakeUrl = BuildUrl(portal.PortalUrl, new Dictionary<string, string>
        {
            ["type"] = "stb",
            ["action"] = "handshake",
            ["token"] = "",
            ["JsHttpRequest"] = "1-xml"
        });

        var handshakeHeaders = GetBaseHeaders(portal.MacAddress);

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
        await GetProfileAsync(portal, token);

        var expiry = DateTime.UtcNow.AddMinutes(600); // 10 hours

        return new PortalSession(token, expiry, serialNumber, deviceId, deviceId2, signature);
    }

    private async Task GetProfileAsync(Portal portal, string token)
    {
        var profileUrl = BuildUrl(portal.PortalUrl, new Dictionary<string, string>
        {
            ["type"] = "stb",
            ["action"] = "get_profile",
            ["JsHttpRequest"] = "1-xml"
        });

        var profileHeaders = GetBaseHeaders(portal.MacAddress);
        profileHeaders["Authorization"] = $"Bearer {token}";

        _logger.LogDebug("Sending get_profile request to {Url}", profileUrl);
        var response = await SendRequestAsync(profileUrl, profileHeaders);
        _logger.LogDebug("Get profile response: {Response}", response);

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

    private string BuildUrl(string baseUrl, Dictionary<string, string> parameters)
    {
        var baseUri = baseUrl.TrimEnd('/');
        var portalPhpUrl = $"{baseUri}/portal.php";

        var uriBuilder = new UriBuilder(portalPhpUrl);
        var query = HttpUtility.ParseQueryString(string.Empty);

        foreach (var param in parameters)
        {
            query[param.Key] = param.Value;
        }

        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    public async Task<List<Channel>> GetChannelsAsync(Portal portal)
    {
        var session = await GetOrCreateSessionAsync(portal);

        var url = BuildUrl(portal.PortalUrl, new Dictionary<string, string>
        {
            ["type"] = "itv",
            ["action"] = "get_ordered_list",
            ["genre"] = "*",
            ["force_ch_link_check"] = "0",
            ["fav"] = "0",
            ["sortby"] = "number",
            ["p"] = "1",
            ["JsHttpRequest"] = "1-xml"
        });

        var headers = GetBaseHeaders(portal.MacAddress);
        headers["Authorization"] = $"Bearer {session.Token}";

        _logger.LogDebug("Getting channels from {Url}", url);
        var response = await SendRequestAsync(url, headers);
        _logger.LogDebug("Channels response: {Response}", response[..Math.Min(500, response.Length)]);

        var result = JsonSerializer.Deserialize<StalkerChannelResponse>(response, _jsonOptions);

        if (result?.Js?.Data == null)
        {
            _logger.LogWarning("No channels found in response or invalid response structure");
            return new List<Channel>();
        }

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

    public async Task<List<string>> GetCategoriesAsync(Portal portal)
    {
        var session = await GetOrCreateSessionAsync(portal);

        var url = BuildUrl(portal.PortalUrl, new Dictionary<string, string>
        {
            ["type"] = "itv",
            ["action"] = "get_genres",
            ["JsHttpRequest"] = "1-xml"
        });

        var headers = GetBaseHeaders(portal.MacAddress);
        headers["Authorization"] = $"Bearer {session.Token}";

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

        // Find the channel by ID to get the command
        var channels = await GetChannelsAsync(portal);
        var channel = channels.FirstOrDefault(c => c.Id == channelId);

        if (channel == null)
        {
            throw new InvalidOperationException($"Channel {channelId} not found");
        }

        var url = BuildUrl(portal.PortalUrl, new Dictionary<string, string>
        {
            ["type"] = "itv",
            ["action"] = "create_link",
            ["cmd"] = channel.Cmd,
            ["series"] = "",
            ["forced_storage"] = "undefined",
            ["disable_ad"] = "0",
            ["download"] = "0",
            ["JsHttpRequest"] = "1-xml"
        });

        var headers = GetBaseHeaders(portal.MacAddress);
        headers["Authorization"] = $"Bearer {session.Token}";

        _logger.LogDebug("Creating stream link from {Url}", url);
        var response = await SendRequestAsync(url, headers);
        _logger.LogDebug("Create link response: {Response}", response);

        var result = JsonSerializer.Deserialize<StalkerLinkResponse>(response, _jsonOptions);

        if (result?.Js?.Url == null)
        {
            throw new InvalidOperationException("Failed to get stream URL from response");
        }

        return result.Js.Url;
    }

    public void ClearSession(string portalId)
    {
        _sessions.Remove(portalId);
    }
}
