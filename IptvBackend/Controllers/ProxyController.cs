using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IptvBackend.Models;
using IptvBackend.Services;

namespace IptvBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProxyController : ControllerBase
{
    private readonly PortalStore _portalStore;
    private readonly StalkerPortalClient _stalkerClient;
    private readonly StreamProxyService _streamProxy;
    private readonly ILogger<ProxyController> _logger;
    private const int BufferSize = 81920; // 80KB buffer for streaming

    public ProxyController(
        PortalStore portalStore, 
        StalkerPortalClient stalkerClient,
        StreamProxyService streamProxy,
        ILogger<ProxyController> logger)
    {
        _portalStore = portalStore;
        _stalkerClient = stalkerClient;
        _streamProxy = streamProxy;
        _logger = logger;
    }

    /// <summary>
    /// Stream a channel through the proxy to bypass CORS
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(
        [FromQuery] string portalId, 
        [FromQuery] string channelId,
        CancellationToken cancellationToken)
    {
        try
        {
            var portal = await _portalStore.GetByIdAsync(portalId);
            if (portal == null)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync("Portal not found");
                return;
            }

            _logger.LogInformation($"Getting stream for channel {channelId} from portal {portal.Name}");

            // Get the stream URL from the portal
            var streamUrl = await _stalkerClient.GetStreamUrlAsync(portal, channelId);
            
            // Update last connected time
            await _portalStore.UpdateLastConnectedAsync(portalId);

            // Determine content type based on URL
            var contentType = GetContentType(streamUrl);

            // Set response headers for streaming
            Response.ContentType = contentType;
            Response.StatusCode = 200;
            
            // Add CORS headers
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            Response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            Response.Headers.Add("Access-Control-Allow-Headers", "Range, Content-Type");
            Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Add("Pragma", "no-cache");
            Response.Headers.Add("Expires", "0");
            
            // Handle range requests for seeking
            if (Request.Headers.TryGetValue("Range", out var rangeHeader))
            {
                _logger.LogInformation($"Range request: {rangeHeader}");
            }

            // Stream the content through the proxy
            await StreamThroughProxy(streamUrl, Response.Body, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stream request cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming channel");
            Response.StatusCode = 500;
            await Response.WriteAsync($"Stream error: {ex.Message}");
        }
    }

    /// <summary>
    /// Direct proxy for any URL - useful for HLS streams or other media
    /// </summary>
    [HttpGet("url")]
    public async Task ProxyUrl([FromQuery] string url, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("URL parameter is required");
                return;
            }

            // Decode URL if needed
            var decodedUrl = Uri.UnescapeDataString(url);
            
            if (!Uri.TryCreate(decodedUrl, UriKind.Absolute, out _))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("Invalid URL format");
                return;
            }

            var contentType = GetContentType(decodedUrl);

            Response.ContentType = contentType;
            Response.StatusCode = 200;
            
            // CORS headers
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            Response.Headers.Add("Cache-Control", "no-cache");

            await StreamThroughProxy(decodedUrl, Response.Body, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Proxy URL request cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying URL");
            Response.StatusCode = 500;
            await Response.WriteAsync($"Proxy error: {ex.Message}");
        }
    }

    private string GetContentType(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "application/octet-stream";
        
        var lowerUrl = url.ToLower();
        
        if (lowerUrl.Contains(".m3u8") || lowerUrl.Contains("m3u8"))
            return "application/vnd.apple.mpegurl";
        if (lowerUrl.Contains(".mp4"))
            return "video/mp4";
        if (lowerUrl.Contains(".ts"))
            return "video/mp2t";
        if (lowerUrl.Contains(".mkv"))
            return "video/x-matroska";
        
        // Default to MPEG-TS for Stalker portals
        return "video/mp2t";
    }

    private async Task StreamThroughProxy(string url, Stream outputStream, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        request.Headers.Add("Accept", "*/*");
        
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var upstreamStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        
        var buffer = new byte[BufferSize];
        int bytesRead;
        
        while ((bytesRead = await upstreamStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            await outputStream.FlushAsync(cancellationToken);
        }
    }
}
