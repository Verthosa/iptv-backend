using IptvBackend.Models;
using System.Net.Http;

namespace IptvBackend.Services;

public class StreamProxyService
{
    private readonly HttpClient _httpClient;
    private const int BufferSize = 65536;

    public StreamProxyService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<Stream> GetStreamAsync(string streamUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            // Return the response stream directly - this will be proxied through ASP.NET Core
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Stream request was cancelled");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to stream: {ex.Message}");
        }
    }

    public async Task<Stream> GetHlsStreamAsync(string hlsUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(hlsUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("HLS stream request was cancelled");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to HLS stream: {ex.Message}");
        }
    }

    public HttpResponseMessage CreateProxyResponse(HttpResponseMessage upstreamResponse, string contentType = "video/mp2t")
    {
        var proxyResponse = new HttpResponseMessage();
        
        // Copy important headers for streaming
        proxyResponse.Content = new PushStreamContent((stream, context) =>
        {
            upstreamResponse.Content.CopyToAsync(stream).Wait();
        });
        
        proxyResponse.StatusCode = upstreamResponse.StatusCode;
        
        // Set streaming headers
        proxyResponse.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        proxyResponse.Content.Headers.ContentLength = upstreamResponse.Content.Headers.ContentLength;
        
        // Add CORS headers for browser access
        proxyResponse.Headers.Add("Access-Control-Allow-Origin", "*");
        proxyResponse.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
        proxyResponse.Headers.Add("Access-Control-Allow-Headers", "Range");
        
        // No cache headers for live content
        proxyResponse.Headers.Add("Cache-Control", "no-cache");
        
        return proxyResponse;
    }
}
