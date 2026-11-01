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
    private readonly string mediaPublicPath;

    public EventsController(RaceDayDbContext dbContext, IConfiguration configuration)
    {
        this.dbContext = dbContext;
        mediaPublicPath = (configuration["EventMedia:PublicPath"] ?? "/EventMedia").TrimEnd('/');
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<EventResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var eventEntities = await dbContext.Events
            .AsNoTracking()
            .Include(eventEntity => eventEntity.Categories)
            .Include(eventEntity => eventEntity.Media)
            .OrderBy(eventEntity => eventEntity.EventDate)
            .ToListAsync(cancellationToken);
        var events = eventEntities.Select(ToResponse).ToList();

        // Both authenticated roles can browse events; no organiser-only role
        // restriction is applied to this read-only endpoint.
        return Ok(events);
    }

    [HttpGet("mine")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(IReadOnlyList<OrganiserEventSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<OrganiserEventSummaryResponse>>> GetMine(
        CancellationToken cancellationToken)
    {
        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var eventEntities = await dbContext.Events
            .AsNoTracking()
            .Where(eventItem => eventItem.OrganizerID == organizerID.Value)
            .Include(eventItem => eventItem.Categories!)
                .ThenInclude(category => category.Entries)
            .Include(eventItem => eventItem.Media)
            .OrderBy(eventItem => eventItem.EventDate)
            .ToListAsync(cancellationToken);
        var events = eventEntities
            .Select(eventItem =>
            {
                var categories = eventItem.Categories ?? [];
                var fees = categories.Select(category => category.EntryFee).ToList();
                return new OrganiserEventSummaryResponse(
                    eventItem.EventID,
                    eventItem.EventName,
                    eventItem.Description,
                    eventItem.EventDate,
                    eventItem.Location,
                    eventItem.DistanceKM,
                    categories
                        .OrderBy(category => string.Equals(
                            category.CategoryName,
                            "General Entry",
                            StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(category => category.EntryFee)
                        .Select(category => (decimal?)category.EntryFee)
                        .FirstOrDefault(),
                    fees.Count == 0 ? null : fees.Min(),
                    fees.Count == 0 ? null : fees.Max(),
                    eventItem.EventType,
                    eventItem.Status,
                    eventItem.RegistrationDeadline,
                    eventItem.CreatedAt,
                    categories.Sum(category => category.Entries?.Count ?? 0),
                    ToMediaResponses(eventItem.Media));
            })
            .ToList();

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
        var eventEntity = await dbContext.Events
            .AsNoTracking()
            .Include(candidate => candidate.Categories)
            .Include(candidate => candidate.Media)
            .FirstOrDefaultAsync(candidate => candidate.EventID == id, cancellationToken);

        return eventEntity is null
            ? NotFound()
            : Ok(ToResponse(eventEntity));
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
            || !IsValidEntryFee(request.EntryFee)
            || request.EventType is null
            || !Enum.IsDefined(request.EventType.Value)
            || request.RegistrationDeadline > request.EventDate
            || !ValidateMedia(request.Media, allowNull: true))
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
            CreatedAt = DateTime.UtcNow,
            Categories =
            [
                new Category
                {
                    CategoryName = "General Entry",
                    EntryFee = request.EntryFee
                }
            ],
            Media = (request.Media ?? [])
                .Select(media => ToMediaEntity(media))
                .ToList()
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
            || !IsValidEntryFee(request.EntryFee)
            || request.EventType is null
            || !Enum.IsDefined(request.EventType.Value)
            || request.RegistrationDeadline > request.EventDate
            || !ValidateMedia(request.Media, allowNull: true))
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
            .Include(candidate => candidate.Categories)
            .Include(candidate => candidate.Media)
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

        if (eventEntity.Status == EventStatus.Cancelled)
        {
            return Conflict(new { message = "Cancelled events cannot be edited." });
        }

        eventEntity.EventName = request.EventName.Trim();
        eventEntity.Description = request.Description.Trim();
        eventEntity.EventDate = request.EventDate;
        eventEntity.Location = request.Location.Trim();
        eventEntity.DistanceKM = request.DistanceKM;
        eventEntity.EventType = request.EventType.Value;
        eventEntity.RegistrationDeadline = request.RegistrationDeadline;

        if (request.Media is not null)
        {
            dbContext.EventMedia.RemoveRange(eventEntity.Media);
            eventEntity.Media = request.Media
                .Select(ToMediaEntity)
                .ToList();
        }

        var generalEntry = eventEntity.Categories?
            .FirstOrDefault(category =>
                string.Equals(category.CategoryName, "General Entry", StringComparison.OrdinalIgnoreCase));
        if (generalEntry is null)
        {
            generalEntry = new Category
            {
                Event = eventEntity,
                CategoryName = "General Entry",
                EntryFee = request.EntryFee
            };
            dbContext.Categories.Add(generalEntry);
        }
        else
        {
            generalEntry.EntryFee = request.EntryFee;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(eventEntity));
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) =>
        SetCancelledAsync(id, cancellationToken);

    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var eventEntity = await dbContext.Events
            .Include(candidate => candidate.Categories!)
                .ThenInclude(category => category.Entries!)
                    .ThenInclude(entry => entry.Result)
            .Include(candidate => candidate.Route)
            .Include(candidate => candidate.Media)
            .FirstOrDefaultAsync(candidate => candidate.EventID == id, cancellationToken);

        if (eventEntity is null)
        {
            return NotFound();
        }

        if (eventEntity.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        var categories = eventEntity.Categories ?? [];
        var entries = categories
            .SelectMany(category => category.Entries ?? [])
            .ToList();
        var results = entries
            .Where(entry => entry.Result is not null)
            .Select(entry => entry.Result!)
            .ToList();

        dbContext.Results.RemoveRange(results);
        dbContext.Entries.RemoveRange(entries);
        dbContext.Categories.RemoveRange(categories);
        if (eventEntity.Route is not null)
        {
            dbContext.Routes.Remove(eventEntity.Route);
        }

        dbContext.Events.Remove(eventEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<IActionResult> SetCancelledAsync(
        int id,
        CancellationToken cancellationToken)
    {
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

        // Check stored ownership before deletion. The route ID selects the
        // event, while the validated JWT identity decides whether it is owned.
        if (eventEntity.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        eventEntity.Status = EventStatus.Cancelled;
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
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
        EntryFee = eventEntity.Categories?
            .OrderBy(category => string.Equals(
                category.CategoryName,
                "General Entry",
                StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(category => category.EntryFee)
            .Select(category => (decimal?)category.EntryFee)
            .FirstOrDefault(),
        MinimumEntryFee = eventEntity.Categories?
            .Select(category => (decimal?)category.EntryFee)
            .Min(),
        MaximumEntryFee = eventEntity.Categories?
            .Select(category => (decimal?)category.EntryFee)
            .Max(),
        Status = eventEntity.Status,
        RegistrationDeadline = eventEntity.RegistrationDeadline,
        CreatedAt = eventEntity.CreatedAt,
        Media = ToMediaResponses(eventEntity.Media)
    };

    private static IReadOnlyList<EventMediaResponse> ToMediaResponses(
        IEnumerable<EventMedia>? media) =>
        (media ?? [])
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new EventMediaResponse(
                item.EventMediaID,
                item.MediaType,
                item.RelativePath,
                item.ContentType,
                item.SizeBytes,
                item.DurationSeconds,
                item.DisplayOrder))
            .ToList();

    private static EventMedia ToMediaEntity(EventMediaRequest media) => new()
    {
        MediaType = media.MediaType,
        RelativePath = media.RelativePath,
        ContentType = media.ContentType,
        SizeBytes = media.SizeBytes,
        DurationSeconds = media.DurationSeconds,
        DisplayOrder = media.DisplayOrder
    };

    private bool ValidateMedia(
        IReadOnlyList<EventMediaRequest>? media,
        bool allowNull)
    {
        if (media is null)
        {
            return allowNull;
        }

        var images = media.Where(item => item.MediaType == EventMediaType.Image).ToList();
        var videos = media.Where(item => item.MediaType == EventMediaType.Video).ToList();
        if (media.Any(item => !Enum.IsDefined(item.MediaType))
            || images.Count > 5
            || videos.Count > 1)
        {
            ModelState.AddModelError(nameof(media), "An event can have at most five images and one video.");
            return false;
        }

        var valid = true;
        foreach (var item in media)
        {
            var contentType = item.ContentType.ToLowerInvariant();
            var extension = Path.GetExtension(item.RelativePath).ToLowerInvariant();
            var validImage = item.MediaType == EventMediaType.Image
                && item.SizeBytes is > 0 and <= 5_242_880
                && item.DurationSeconds is null
                && ((contentType == "image/jpeg" && extension is ".jpg" or ".jpeg")
                    || (contentType == "image/png" && extension == ".png")
                    || (contentType == "image/webp" && extension == ".webp"));
            var validVideo = item.MediaType == EventMediaType.Video
                && item.SizeBytes is > 0 and <= 20_971_520
                && item.DurationSeconds is > 0 and <= 30
                && ((contentType == "video/mp4" && extension == ".mp4")
                    || (contentType == "video/webm" && extension == ".webm"));
            var validPath = item.RelativePath.StartsWith($"{mediaPublicPath}/", StringComparison.Ordinal)
                && !item.RelativePath.Contains("..", StringComparison.Ordinal)
                && !item.RelativePath.Contains('\\')
                && item.RelativePath.Length <= 100;
            if (validPath)
            {
                var filename = item.RelativePath[(mediaPublicPath.Length + 1)..];
                var extensionForName = Path.GetExtension(filename);
                validPath = string.Equals(
                    filename,
                    Path.GetFileName(filename),
                    StringComparison.Ordinal)
                    && Guid.TryParseExact(Path.GetFileNameWithoutExtension(filename), "N", out _)
                    && extensionForName is ".jpg" or ".jpeg" or ".png" or ".webp" or ".mp4" or ".webm";
            }

            if (!validPath || !(validImage || validVideo) || item.DisplayOrder < 0)
            {
                ModelState.AddModelError(nameof(media), "One or more media references are invalid.");
                valid = false;
                break;
            }
        }

        if (media.Select(item => item.RelativePath).Distinct(StringComparer.Ordinal).Count() != media.Count)
        {
            ModelState.AddModelError(nameof(media), "Media paths must be unique.");
            valid = false;
        }

        return valid;
    }

    private static bool IsValidEntryFee(decimal entryFee) =>
        entryFee >= 0
        && entryFee <= 99999999.99m
        && decimal.Round(entryFee, 2) == entryFee;
}
