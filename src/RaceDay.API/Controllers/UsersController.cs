using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;

namespace RaceDay.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly RaceDayDbContext dbContext;

    public UsersController(RaceDayDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> GetCurrentProfile(
        CancellationToken cancellationToken)
    {
        var userID = GetAuthenticatedUserID();
        if (userID is null)
        {
            return Unauthorized();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.UserID == userID.Value, cancellationToken);

        return user is null
            ? NotFound()
            : Ok(ToProfileResponse(user));
    }

    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserProfileResponse>> UpdateCurrentProfile(
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userID = GetAuthenticatedUserID();
        if (userID is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.FirstName)
            || string.IsNullOrWhiteSpace(request.LastName)
            || string.IsNullOrWhiteSpace(request.Email))
        {
            ModelState.AddModelError(string.Empty, "First name, last name, and email are required.");
            return ValidationProblem(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.UserID == userID.Value, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        // Reject an email already owned by another account while preserving the
        // same normalization rule used by registration and login.
        var emailExists = await dbContext.Users
            .AnyAsync(candidate => candidate.Email == normalizedEmail && candidate.UserID != userID.Value, cancellationToken);

        if (emailExists)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }

        // Only editable profile fields are changed. Identity, role, password
        // hash, and creation timestamp remain controlled by the server.
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = normalizedEmail;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToProfileResponse(user));
    }

    private int? GetAuthenticatedUserID()
    {
        var userIDClaim = User?.FindFirstValue("UserID");
        return int.TryParse(userIDClaim, out var userID) && userID > 0
            ? userID
            : null;
    }

    private static UserProfileResponse ToProfileResponse(User user) => new()
    {
        UserID = user.UserID,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        Role = user.Role,
        CreatedAt = user.CreatedAt
    };
}
