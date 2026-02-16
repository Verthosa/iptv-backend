using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IptvBackend.Models;
using IptvBackend.Services;

namespace IptvBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PortalsController : ControllerBase
{
    private readonly PortalStore _portalStore;
    private readonly StalkerPortalClient _stalkerClient;

    public PortalsController(PortalStore portalStore, StalkerPortalClient stalkerClient)
    {
        _portalStore = portalStore;
        _stalkerClient = stalkerClient;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<Portal>>>> GetPortals()
    {
        try
        {
            var portals = await _portalStore.GetAllAsync();
            return Ok(new ApiResponse<List<Portal>>
            {
                Success = true,
                Data = portals,
                Message = "Portals retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<List<Portal>>
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<Portal>>> GetPortal(string id)
    {
        try
        {
            var portal = await _portalStore.GetByIdAsync(id);
            if (portal == null)
            {
                return NotFound(new ApiResponse<Portal>
                {
                    Success = false,
                    Error = "Portal not found"
                });
            }

            return Ok(new ApiResponse<Portal>
            {
                Success = true,
                Data = portal,
                Message = "Portal retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<Portal>
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Portal>>> CreatePortal([FromBody] PortalCreate request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name) || 
                string.IsNullOrWhiteSpace(request.PortalUrl) || 
                string.IsNullOrWhiteSpace(request.MacAddress))
            {
                return BadRequest(new ApiResponse<Portal>
                {
                    Success = false,
                    Error = "Name, Portal URL, and MAC Address are required"
                });
            }

            // Validate URL format
            if (!Uri.TryCreate(request.PortalUrl, UriKind.Absolute, out var uri))
            {
                return BadRequest(new ApiResponse<Portal>
                {
                    Success = false,
                    Error = "Invalid portal URL format"
                });
            }

            // Validate MAC address format (basic check)
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.MacAddress, @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$"))
            {
                return BadRequest(new ApiResponse<Portal>
                {
                    Success = false,
                    Error = "Invalid MAC address format"
                });
            }

            var portal = new Portal
            {
                Name = request.Name,
                PortalUrl = request.PortalUrl,
                MacAddress = request.MacAddress.ToUpper()
            };

            var created = await _portalStore.AddAsync(portal);
            
            return CreatedAtAction(nameof(GetPortal), new { id = created.Id }, new ApiResponse<Portal>
            {
                Success = true,
                Data = created,
                Message = "Portal created successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<Portal>
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<Portal>>> UpdatePortal(string id, [FromBody] PortalUpdate request)
    {
        try
        {
            var portal = await _portalStore.UpdateAsync(id, request);
            if (portal == null)
            {
                return NotFound(new ApiResponse<Portal>
                {
                    Success = false,
                    Error = "Portal not found"
                });
            }

            // Clear session if credentials changed
            if (request.PortalUrl != null || request.MacAddress != null)
            {
                _stalkerClient.ClearSession(id);
            }

            return Ok(new ApiResponse<Portal>
            {
                Success = true,
                Data = portal,
                Message = "Portal updated successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<Portal>
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> DeletePortal(string id)
    {
        try
        {
            var deleted = await _portalStore.DeleteAsync(id);
            if (!deleted)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Error = "Portal not found"
                });
            }

            // Clear session
            _stalkerClient.ClearSession(id);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Portal deleted successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    [HttpPost("{id}/test")]
    public async Task<ActionResult<ApiResponse<object>>> TestPortal(string id)
    {
        try
        {
            var portal = await _portalStore.GetByIdAsync(id);
            if (portal == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Error = "Portal not found"
                });
            }

            // Test connection by getting channels
            var channels = await _stalkerClient.GetChannelsAsync(portal);

            await _portalStore.UpdateLastConnectedAsync(id);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = new { channelCount = channels.Count },
                Message = $"Portal connected successfully. Found {channels.Count} channels."
            });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = $"HTTP connection failed: {ex.Message}. Please verify the portal URL is correct and accessible."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = $"Portal authentication failed: {ex.Message}. Please verify your MAC address is correct and authorized."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = $"Connection failed: {ex.Message}"
            });
        }
    }
}
