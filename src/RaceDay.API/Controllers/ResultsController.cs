using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/results")]
[Authorize]
public sealed class ResultsController : ControllerBase
{
    private readonly RaceDayDbContext dbContext;

    public ResultsController(RaceDayDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpPost("~/api/entries/{entryID:int}/results")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResultResponse>> Create(
        int entryID,
        CreateResultRequest request,
        CancellationToken cancellationToken)
    {
        if (request.EntryID != entryID || !IsValid(request.FinishTime, request.OverallPosition, request.CategoryPosition, request.ResultStatus))
        {
            return InvalidResultData();
        }

        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var entry = await GetEntryWithEvent(entryID, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.Category.Event.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        if (entry.Result is not null)
        {
            return Conflict(new { message = "A result already exists for this entry." });
        }

        var result = new Result
        {
            EntryID = entry.EntryID,
            FinishTime = request.FinishTime,
            OverallPosition = request.OverallPosition,
            CategoryPosition = request.CategoryPosition,
            ResultStatus = request.ResultStatus!.Value,
            RecordedAt = DateTime.UtcNow
        };

        dbContext.Results.Add(result);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Created($"/api/results/{result.ResultID}", ToResponse(result, entry));
    }

    [HttpPut("{resultID:int}")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResultResponse>> Update(
        int resultID,
        UpdateResultRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValid(request.FinishTime, request.OverallPosition, request.CategoryPosition, request.ResultStatus))
        {
            return InvalidResultData();
        }

        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var result = await dbContext.Results
            .Include(resultItem => resultItem.Entry)
                .ThenInclude(entry => entry.Category)
                    .ThenInclude(category => category.Event)
            .FirstOrDefaultAsync(resultItem => resultItem.ResultID == resultID, cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        if (result.Entry.Category.Event.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        result.FinishTime = request.FinishTime;
        result.OverallPosition = request.OverallPosition;
        result.CategoryPosition = request.CategoryPosition;
        result.ResultStatus = request.ResultStatus!.Value;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(result, result.Entry));
    }

    [HttpGet("me")]
    [Authorize(Roles = nameof(UserRole.Participant))]
    [ProducesResponseType(typeof(IReadOnlyList<ResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ResultResponse>>> GetMine(
        CancellationToken cancellationToken)
    {
        var userID = GetAuthenticatedUserID();
        if (userID is null)
        {
            return Unauthorized();
        }

        var results = await dbContext.Results
            .AsNoTracking()
            .Where(result => result.Entry.UserID == userID.Value)
            .Select(result => new ResultResponse
            {
                ResultID = result.ResultID,
                EntryID = result.EntryID,
                ParticipantUserID = result.Entry.UserID,
                EventID = result.Entry.Category.EventID,
                EventName = result.Entry.Category.Event.EventName,
                CategoryID = result.Entry.CategoryID,
                CategoryName = result.Entry.Category.CategoryName,
                FinishTime = result.FinishTime,
                OverallPosition = result.OverallPosition,
                CategoryPosition = result.CategoryPosition,
                ResultStatus = result.ResultStatus,
                RecordedAt = result.RecordedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(results);
    }

    [HttpGet("~/api/events/{eventID:int}/results")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(IReadOnlyList<ResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ResultResponse>>> GetForEvent(
        int eventID,
        CancellationToken cancellationToken)
    {
        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var eventExists = await dbContext.Events
            .AsNoTracking()
            .AnyAsync(eventItem => eventItem.EventID == eventID, cancellationToken);
        if (!eventExists)
        {
            return NotFound();
        }

        var ownsEvent = await dbContext.Events
            .AsNoTracking()
            .AnyAsync(eventItem => eventItem.EventID == eventID && eventItem.OrganizerID == organizerID.Value, cancellationToken);
        if (!ownsEvent)
        {
            return Forbid();
        }

        var results = await dbContext.Results
            .AsNoTracking()
            .Where(result => result.Entry.Category.EventID == eventID)
            .Select(result => new ResultResponse
            {
                ResultID = result.ResultID,
                EntryID = result.EntryID,
                ParticipantUserID = result.Entry.UserID,
                EventID = result.Entry.Category.EventID,
                EventName = result.Entry.Category.Event.EventName,
                CategoryID = result.Entry.CategoryID,
                CategoryName = result.Entry.Category.CategoryName,
                FinishTime = result.FinishTime,
                OverallPosition = result.OverallPosition,
                CategoryPosition = result.CategoryPosition,
                ResultStatus = result.ResultStatus,
                RecordedAt = result.RecordedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(results);
    }

    [HttpGet("{resultID:int}")]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResultResponse>> GetByID(
        int resultID,
        CancellationToken cancellationToken)
    {
        var userID = GetAuthenticatedUserID();
        if (userID is null)
        {
            return Unauthorized();
        }

        var result = await dbContext.Results
            .AsNoTracking()
            .Where(resultItem => resultItem.ResultID == resultID)
            .Select(resultItem => new ResultResponse
            {
                ResultID = resultItem.ResultID,
                EntryID = resultItem.EntryID,
                ParticipantUserID = resultItem.Entry.UserID,
                EventID = resultItem.Entry.Category.EventID,
                EventName = resultItem.Entry.Category.Event.EventName,
                CategoryID = resultItem.Entry.CategoryID,
                CategoryName = resultItem.Entry.Category.CategoryName,
                FinishTime = resultItem.FinishTime,
                OverallPosition = resultItem.OverallPosition,
                CategoryPosition = resultItem.CategoryPosition,
                ResultStatus = resultItem.ResultStatus,
                RecordedAt = resultItem.RecordedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        var isParticipant = User.IsInRole(nameof(UserRole.Participant)) && result.ParticipantUserID == userID.Value;
        var isOrganizer = User.IsInRole(nameof(UserRole.Organiser)) && await dbContext.Events
            .AsNoTracking()
            .AnyAsync(eventItem => eventItem.EventID == result.EventID && eventItem.OrganizerID == userID.Value, cancellationToken);

        return isParticipant || isOrganizer
            ? Ok(result)
            : Forbid();
    }

    private async Task<Entry?> GetEntryWithEvent(int entryID, CancellationToken cancellationToken) =>
        await dbContext.Entries
            .Include(entry => entry.Category)
                .ThenInclude(category => category.Event)
            .Include(entry => entry.Result)
            .FirstOrDefaultAsync(entry => entry.EntryID == entryID, cancellationToken);

    private int? GetAuthenticatedUserID()
    {
        var userIDClaim = User?.FindFirstValue("UserID");
        return int.TryParse(userIDClaim, out var userID) && userID > 0
            ? userID
            : null;
    }

    private ActionResult InvalidResultData()
    {
        ModelState.AddModelError(string.Empty, "The result details are invalid.");
        return ValidationProblem(ModelState);
    }

    private static bool IsValid(TimeSpan finishTime, int overallPosition, int categoryPosition, ResultStatus? status) =>
        finishTime >= TimeSpan.Zero
        && overallPosition > 0
        && categoryPosition > 0
        && status is not null
        && Enum.IsDefined(status.Value);

    private static ResultResponse ToResponse(Result result, Entry entry) => new()
    {
        ResultID = result.ResultID,
        EntryID = result.EntryID,
        ParticipantUserID = entry.UserID,
        EventID = entry.Category.EventID,
        EventName = entry.Category.Event.EventName,
        CategoryID = entry.CategoryID,
        CategoryName = entry.Category.CategoryName,
        FinishTime = result.FinishTime,
        OverallPosition = result.OverallPosition,
        CategoryPosition = result.CategoryPosition,
        ResultStatus = result.ResultStatus,
        RecordedAt = result.RecordedAt
    };
}
