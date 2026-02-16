using IptvBackend.Models;

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

}
