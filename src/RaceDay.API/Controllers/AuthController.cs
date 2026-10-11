using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using RaceDay.API.Services;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly RaceDayDbContext dbContext;
    private readonly PasswordHashService passwordHashService;
    private readonly JwtTokenService jwtTokenService;
    private readonly IConfiguration configuration;

    public AuthController(
        RaceDayDbContext dbContext,
        PasswordHashService passwordHashService,
        JwtTokenService jwtTokenService,
        IConfiguration configuration)
    {
        this.dbContext = dbContext;
        this.passwordHashService = passwordHashService;
        this.jwtTokenService = jwtTokenService;
        this.configuration = configuration;
    }

    // Registration must be available before a user has an account or JWT.
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisteredUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisteredUserResponse>> Register(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Role is null || !Enum.IsDefined(request.Role.Value))
        {
            ModelState.AddModelError(nameof(request.Role), "Role must be Organiser or Participant.");
            return ValidationProblem(ModelState);
        }

        if (request.Role == UserRole.Organiser
            && !HasValidOrganiserRegistrationCode(request.OrganiserRegistrationCode))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = "Organiser registration is unavailable or the invitation code is invalid." });
        }

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        // Reject duplicate normalized emails so casing differences cannot create
        // multiple accounts for the same email address.
        var emailExists = await dbContext.Users
            .AnyAsync(user => user.Email == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = normalizedEmail,
            Role = request.Role.Value,
            CreatedAt = DateTime.UtcNow
        };

        // Only the one-way hash is assigned to the entity. The plain-text
        // password is never stored, logged, or included in an API response.
        user.PasswordHash = passwordHashService.HashPassword(user, request.Password);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Return a dedicated DTO so the persistence-only PasswordHash is never
        // exposed to the client.
        var response = new RegisteredUserResponse
        {
            UserID = user.UserID,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        };

        return Created($"/api/auth/{user.UserID}", response);
    }

    private bool HasValidOrganiserRegistrationCode(string? suppliedCode)
    {
        if (suppliedCode is null
            || suppliedCode.Length != 4
            || !suppliedCode.All(char.IsAsciiDigit))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedCode));
        var configuredCodes = configuration
            .GetSection("Registration:OrganiserCodes")
            .GetChildren()
            .Select(section => section.Value)
            .Append(configuration["Registration:OrganiserCode"]);

        var isValid = false;
        foreach (var configuredCode in configuredCodes)
        {
            if (string.IsNullOrEmpty(configuredCode))
            {
                continue;
            }

            var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredCode));
            isValid |= CryptographicOperations.FixedTimeEquals(configuredHash, suppliedHash);
        }

        return isValid;
    }

    /// <summary>
    /// Verifies credentials, creates a signed JWT, and returns safe account
    /// details for the authenticated client.
    /// </summary>
    // Login must be available before a user has an authenticated JWT.
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginUserResponse>> Login(
        LoginUserRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();

        var user = await dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == normalizedEmail, cancellationToken);

        // Use the same response for an unknown account and an incorrect password
        // so the endpoint does not reveal which email addresses are registered.
        if (user is null || !passwordHashService.VerifyPassword(user, request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var token = jwtTokenService.GenerateToken(user);

        // Return only public account details and the signed token. PasswordHash
        // and the original password are never exposed in the response.
        var response = new LoginUserResponse
        {
            UserID = user.UserID,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Role = user.Role,
            Token = token
        };

        return Ok(response);
    }
}
