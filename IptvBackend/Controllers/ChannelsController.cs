using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IptvBackend.Models;
using IptvBackend.Services;

namespace IptvBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChannelsController : ControllerBase
{
    private readonly PortalStore _portalStore;
    private readonly StalkerPortalClient _stalkerClient;

    public ChannelsController(PortalStore portalStore, StalkerPortalClient stalkerClient)
    {
        _portalStore = portalStore;
        _stalkerClient = stalkerClient;
    }

    [HttpGet("portal/{portalId}")]
    public async Task<ActionResult<ApiResponse<ChannelResponse>>> GetChannels(string portalId, [FromQuery] string? categoryId = null)
    {
        try
        {
            var portal = await _portalStore.GetByIdAsync(portalId);
            if (portal == null)
            {
                return NotFound(new ApiResponse<ChannelResponse>
                {
                    Success = false,
                    Error = "Portal not found"
                });
            }

            var allChannels = await _stalkerClient.GetChannelsAsync(portal);

            // Filter by category ID if specified
            var filteredChannels = string.IsNullOrWhiteSpace(categoryId)
                ? allChannels
                : allChannels.Where(c => c.CategoryId.Equals(categoryId, StringComparison.OrdinalIgnoreCase)).ToList();

            var response = new ChannelResponse
            {
                Channels = filteredChannels.OrderBy(c => c.Number).ToList()
            };

            return Ok(new ApiResponse<ChannelResponse>
            {
                Success = true,
                Data = response,
                Message = $"Retrieved {response.Channels.Count} channels"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<ChannelResponse>
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    [HttpGet("portal/{portalId}/categories")]
    public async Task<ActionResult<ApiResponse<CategoriesResponse>>> GetCategories(string portalId)
    {
        try
        {
            var portal = await _portalStore.GetByIdAsync(portalId);
            if (portal == null)
            {
                return NotFound(new ApiResponse<CategoriesResponse>
                {
                    Success = false,
                    Error = "Portal not found"
                });
            }

            var categories = await _stalkerClient.GetCategoriesAsync(portal);

            var response = new CategoriesResponse
            {
                Categories = categories.OrderBy(c => c.Title).ToList()
            };

            return Ok(new ApiResponse<CategoriesResponse>
            {
                Success = true,
                Data = response,
                Message = $"Found {categories.Count} categories"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<CategoriesResponse>
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    [HttpGet("portal/{portalId}/search")]
    public async Task<ActionResult<ApiResponse<ChannelResponse>>> SearchChannels(string portalId, [FromQuery] string q)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest(new ApiResponse<ChannelResponse>
                {
                    Success = false,
                    Error = "Search query is required"
                });
            }

            var portal = await _portalStore.GetByIdAsync(portalId);
            if (portal == null)
            {
                return NotFound(new ApiResponse<ChannelResponse>
                {
                    Success = false,
                    Error = "Portal not found"
                });
            }

            var channels = await _stalkerClient.GetChannelsAsync(portal);
            var searchResults = channels
                .Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Number)
                .ToList();

            var response = new ChannelResponse
            {
                Channels = searchResults
            };

            return Ok(new ApiResponse<ChannelResponse>
            {
                Success = true,
                Data = response,
                Message = $"Found {searchResults.Count} channels matching '{q}'"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<ChannelResponse>
            {
                Success = false,
                Error = ex.Message
            });
        }
    }
}
