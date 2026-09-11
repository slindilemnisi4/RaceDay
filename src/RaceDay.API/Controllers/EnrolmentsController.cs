using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/enrolments")]
[Authorize]
public sealed class EnrolmentsController : ControllerBase
{
    private readonly RaceDayDbContext dbContext;

    public EnrolmentsController(RaceDayDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpPost("~/api/events/{eventID:int}/enrolments")]
    [Authorize(Roles = nameof(UserRole.Participant))]
    [ProducesResponseType(typeof(EnrolmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EnrolmentResponse>> Create(
        int eventID,
        CreateEnrolmentRequest request,
        CancellationToken cancellationToken)
    {
        var userID = GetAuthenticatedUserID();
        if (userID is null)
        {
            return Unauthorized();
        }

        var category = await dbContext.Categories
            .Include(categoryItem => categoryItem.Event)
            .FirstOrDefaultAsync(categoryItem => categoryItem.CategoryID == request.CategoryID, cancellationToken);

        if (category is null || category.Event is null || category.Event.EventID != eventID)
        {
            return NotFound();
        }

        if (category.Event.RegistrationDeadline < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Conflict(new { message = "Registration for this event is closed." });
        }

        var duplicateExists = await dbContext.Entries
            .AnyAsync(entry => entry.UserID == userID.Value && entry.CategoryID == request.CategoryID, cancellationToken);

        if (duplicateExists)
        {
            return Conflict(new { message = "The participant is already enrolled in this category." });
        }

        if (category.MaxParticipants.HasValue)
        {
            var enrolmentCount = await dbContext.Entries
                .CountAsync(entry => entry.CategoryID == request.CategoryID, cancellationToken);

            if (enrolmentCount >= category.MaxParticipants.Value)
            {
                return Conflict(new { message = "This category is full." });
            }
        }

        var entry = new Entry
        {
            UserID = userID.Value,
            CategoryID = category.CategoryID,
            EntryDate = DateTime.UtcNow
        };

        dbContext.Entries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new EnrolmentResponse
        {
            EntryID = entry.EntryID,
            EventID = category.EventID,
            EventName = category.Event.EventName,
            CategoryID = category.CategoryID,
            CategoryName = category.CategoryName,
            EntryDate = entry.EntryDate
        };

        return Created($"/api/events/{eventID}/enrolments/{entry.EntryID}", response);
    }

    [HttpGet("me")]
    [Authorize(Roles = nameof(UserRole.Participant))]
    [ProducesResponseType(typeof(IReadOnlyList<EnrolmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<EnrolmentResponse>>> GetMine(
        CancellationToken cancellationToken)
    {
        var userID = GetAuthenticatedUserID();
        if (userID is null)
        {
            return Unauthorized();
        }

        // UserID comes only from the validated JWT, so the request cannot select
        // another participant's enrolments through a route or query parameter.
        var enrolments = await dbContext.Entries
            .AsNoTracking()
            .Where(entry => entry.UserID == userID.Value)
            .Select(entry => new EnrolmentResponse
            {
                EntryID = entry.EntryID,
                EventID = entry.Category.EventID,
                EventName = entry.Category.Event.EventName,
                CategoryID = entry.CategoryID,
                CategoryName = entry.Category.CategoryName,
                EntryDate = entry.EntryDate
            })
            .ToListAsync(cancellationToken);

        return Ok(enrolments);
    }

    [HttpGet("~/api/events/{eventID:int}/enrolments")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(IReadOnlyList<EnrolmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EnrolmentResponse>>> GetForEvent(
        int eventID,
        CancellationToken cancellationToken)
    {
        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var eventEntity = await dbContext.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(eventItem => eventItem.EventID == eventID, cancellationToken);

        if (eventEntity is null)
        {
            return NotFound();
        }

        // Entries do not store EventID. Ownership is checked on the Event first,
        // then enrolments are filtered through Entry -> Category -> Event.
        if (eventEntity.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        var enrolments = await dbContext.Entries
            .AsNoTracking()
            .Where(entry => entry.Category.EventID == eventID)
            .Select(entry => new EnrolmentResponse
            {
                EntryID = entry.EntryID,
                EventID = entry.Category.EventID,
                EventName = entry.Category.Event.EventName,
                CategoryID = entry.CategoryID,
                CategoryName = entry.Category.CategoryName,
                EntryDate = entry.EntryDate
            })
            .ToListAsync(cancellationToken);

        return Ok(enrolments);
    }

    private int? GetAuthenticatedUserID()
    {
        var userIDClaim = User?.FindFirstValue("UserID");
        return int.TryParse(userIDClaim, out var userID) && userID > 0
            ? userID
            : null;
    }

    private static EnrolmentResponse ToResponse(Entry entry) => new()
    {
        EntryID = entry.EntryID,
        EventID = entry.Category.EventID,
        EventName = entry.Category.Event.EventName,
        CategoryID = entry.CategoryID,
        CategoryName = entry.Category.CategoryName,
        EntryDate = entry.EntryDate
    };
}
