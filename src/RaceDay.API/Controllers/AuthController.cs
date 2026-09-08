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

    public AuthController(
        RaceDayDbContext dbContext,
        PasswordHashService passwordHashService)
    {
        this.dbContext = dbContext;
        this.passwordHashService = passwordHashService;
    }

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

    /// <summary>
    /// Verifies credentials and returns safe account details for the next
    /// authentication step. JWT creation is intentionally handled separately.
    /// </summary>
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

        // Return only public account details. PasswordHash and any future token
        // are deliberately excluded until the later JWT objective.
        var response = new LoginUserResponse
        {
            UserID = user.UserID,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Role = user.Role
        };

        return Ok(response);
    }
}
