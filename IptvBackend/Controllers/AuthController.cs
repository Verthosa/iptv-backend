using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using IptvBackend.Models;
using IptvBackend.Services;

namespace IptvBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserStore _userStore;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserStore userStore, ILogger<AuthController> logger)
    {
        _userStore = userStore;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Error = "Username and password are required"
                });
            }

            if (request.Username.Length < 3)
            {
                return BadRequest(new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Error = "Username must be at least 3 characters"
                });
            }

            if (request.Password.Length < 6)
            {
                return BadRequest(new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Error = "Password must be at least 6 characters"
                });
            }

            if (await _userStore.UsernameExistsAsync(request.Username))
            {
                return BadRequest(new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Error = "Username already exists"
                });
            }

            var user = await _userStore.CreateAsync(request.Username, request.Password, request.Email);

            _logger.LogInformation($"User registered: {user.Username}");

            // Auto-login after registration
            await SignInUser(user);

            return Ok(new ApiResponse<AuthResponse>
            {
                Success = true,
                Data = MapToAuthResponse(user),
                Message = "Registration successful"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, new ApiResponse<AuthResponse>
            {
                Success = false,
                Error = "An error occurred during registration"
            });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Error = "Username and password are required"
                });
            }

            var user = await _userStore.ValidateCredentialsAsync(request.Username, request.Password);

            if (user == null)
            {
                return Unauthorized(new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Error = "Invalid username or password"
                });
            }

            await SignInUser(user);

            _logger.LogInformation($"User logged in: {user.Username}");

            return Ok(new ApiResponse<AuthResponse>
            {
                Success = true,
                Data = MapToAuthResponse(user),
                Message = "Login successful"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new ApiResponse<AuthResponse>
            {
                Success = false,
                Error = "An error occurred during login"
            });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> Logout()
    {
        try
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            _logger.LogInformation("User logged out");

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Logout successful"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Error = "An error occurred during logout"
            });
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> GetCurrentUser()
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Error = "Not authenticated"
                });
            }

            var user = await _userStore.GetByIdAsync(userId);

            if (user == null)
            {
                return Unauthorized(new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Error = "User not found"
                });
            }

            return Ok(new ApiResponse<AuthResponse>
            {
                Success = true,
                Data = MapToAuthResponse(user),
                Message = "User retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            return StatusCode(500, new ApiResponse<AuthResponse>
            {
                Success = false,
                Error = "An error occurred"
            });
        }
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Error = "Current password and new password are required"
                });
            }

            if (request.NewPassword.Length < 6)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Error = "New password must be at least 6 characters"
                });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Error = "Not authenticated"
                });
            }

            var user = await _userStore.GetByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Error = "User not found"
                });
            }

            // Verify current password
            var validatedUser = await _userStore.ValidateCredentialsAsync(user.Username, request.CurrentPassword);
            if (validatedUser == null)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Error = "Current password is incorrect"
                });
            }

            var success = await _userStore.ChangePasswordAsync(userId, request.NewPassword);

            if (!success)
            {
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Error = "Failed to change password"
                });
            }

            _logger.LogInformation($"Password changed for user: {user.Username}");

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Password changed successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Error = "An error occurred"
            });
        }
    }

    private async Task SignInUser(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
    }

    private static AuthResponse MapToAuthResponse(User user)
    {
        return new AuthResponse
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            CreatedAt = user.CreatedAt
        };
    }
}
