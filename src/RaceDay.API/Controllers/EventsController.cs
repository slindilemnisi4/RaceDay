using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/events")]
[Authorize(Roles = nameof(UserRole.Organiser))]
public sealed class EventsController : ControllerBase
{
    private readonly RaceDayDbContext dbContext;

    public EventsController(RaceDayDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpPost]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> Create(
        CreateEventRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EventName)
            || string.IsNullOrWhiteSpace(request.Description)
            || string.IsNullOrWhiteSpace(request.Location)
            || request.EventDate == default
            || request.RegistrationDeadline == default
            || request.DistanceKM <= 0
            || request.EventType is null
            || !Enum.IsDefined(request.EventType.Value)
            || request.RegistrationDeadline > request.EventDate)
        {
            ModelState.AddModelError(string.Empty, "The event details are invalid.");
            return ValidationProblem(ModelState);
        }

        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        // The organizer identity comes from the validated JWT claim, never from
        // client input, so an organiser cannot create an event for another user.
        var organizerExists = await dbContext.Users
            .AnyAsync(user => user.UserID == organizerID.Value, cancellationToken);

        if (!organizerExists)
        {
            return NotFound();
        }

        var eventEntity = new Event
        {
            OrganizerID = organizerID.Value,
            EventName = request.EventName.Trim(),
            Description = request.Description.Trim(),
            EventDate = request.EventDate,
            Location = request.Location.Trim(),
            DistanceKM = request.DistanceKM,
            EventType = request.EventType.Value,
            RegistrationDeadline = request.RegistrationDeadline,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Events.Add(eventEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(eventEntity);
        return Created($"/api/events/{eventEntity.EventID}", response);
    }

    private int? GetAuthenticatedUserID()
    {
        var userIDClaim = User?.FindFirstValue("UserID");
        return int.TryParse(userIDClaim, out var userID) && userID > 0
            ? userID
            : null;
    }

    private static EventResponse ToResponse(Event eventEntity) => new()
    {
        EventID = eventEntity.EventID,
        OrganizerID = eventEntity.OrganizerID,
        EventName = eventEntity.EventName,
        Description = eventEntity.Description,
        EventDate = eventEntity.EventDate,
        Location = eventEntity.Location,
        DistanceKM = eventEntity.DistanceKM,
        EventType = eventEntity.EventType,
        RegistrationDeadline = eventEntity.RegistrationDeadline,
        CreatedAt = eventEntity.CreatedAt
    };
}
