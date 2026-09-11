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
[Authorize]
public sealed class EventsController : ControllerBase
{
    private readonly RaceDayDbContext dbContext;

    public EventsController(RaceDayDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<EventResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var events = await dbContext.Events
            .AsNoTracking()
            .OrderBy(eventEntity => eventEntity.EventDate)
            .Select(eventEntity => ToResponse(eventEntity))
            .ToListAsync(cancellationToken);

        // Both authenticated roles can browse events; no organiser-only role
        // restriction is applied to this read-only endpoint.
        return Ok(events);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> GetByID(
        int id,
        CancellationToken cancellationToken)
    {
        var eventResponse = await dbContext.Events
            .AsNoTracking()
            .Where(eventEntity => eventEntity.EventID == id)
            .Select(eventEntity => ToResponse(eventEntity))
            .FirstOrDefaultAsync(cancellationToken);

        return eventResponse is null
            ? NotFound()
            : Ok(eventResponse);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Organiser))]
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

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> Update(
        int id,
        UpdateEventRequest request,
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

        var eventEntity = await dbContext.Events
            .FirstOrDefaultAsync(candidate => candidate.EventID == id, cancellationToken);

        if (eventEntity is null)
        {
            return NotFound();
        }

        // Ownership is checked against the stored OrganizerID and the validated
        // JWT identity. OrganizerID is never accepted from the update request.
        if (eventEntity.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        eventEntity.EventName = request.EventName.Trim();
        eventEntity.Description = request.Description.Trim();
        eventEntity.EventDate = request.EventDate;
        eventEntity.Location = request.Location.Trim();
        eventEntity.DistanceKM = request.DistanceKM;
        eventEntity.EventType = request.EventType.Value;
        eventEntity.RegistrationDeadline = request.RegistrationDeadline;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(eventEntity));
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
