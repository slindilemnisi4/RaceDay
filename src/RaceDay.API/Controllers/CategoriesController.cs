using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public sealed class CategoriesController : ControllerBase
{
    private readonly RaceDayDbContext dbContext;

    public CategoriesController(RaceDayDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories
            .AsNoTracking()
            .OrderBy(category => category.CategoryID)
            .Select(category => ToResponse(category))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> GetByID(
        int id,
        CancellationToken cancellationToken)
    {
        var response = await dbContext.Categories
            .AsNoTracking()
            .Where(category => category.CategoryID == id)
            .Select(category => ToResponse(category))
            .FirstOrDefaultAsync(cancellationToken);

        return response is null
            ? NotFound()
            : Ok(response);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> Create(
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValid(request.CategoryName, request.DistanceKM, request.MaxParticipants, request.EntryFee))
        {
            return InvalidCategoryData();
        }

        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var eventEntity = await dbContext.Events
            .FirstOrDefaultAsync(eventItem => eventItem.EventID == request.EventID, cancellationToken);

        if (eventEntity is null)
        {
            return NotFound();
        }

        // Category ownership is inherited from its Event. The JWT identity must
        // match the Event organizer before a category can be created.
        if (eventEntity.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        var category = new Category
        {
            EventID = eventEntity.EventID,
            CategoryName = request.CategoryName.Trim(),
            DistanceKM = request.DistanceKM,
            MaxParticipants = request.MaxParticipants,
            EntryFee = request.EntryFee
        };

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Created($"/api/categories/{category.CategoryID}", ToResponse(category));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> Update(
        int id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValid(request.CategoryName, request.DistanceKM, request.MaxParticipants, request.EntryFee))
        {
            return InvalidCategoryData();
        }

        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var category = await dbContext.Categories
            .Include(categoryItem => categoryItem.Event)
            .FirstOrDefaultAsync(categoryItem => categoryItem.CategoryID == id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        // Keep the category attached to its existing Event and authorize through
        // that Event's organizer rather than trusting any client identity.
        if (category.Event.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        category.CategoryName = request.CategoryName.Trim();
        category.DistanceKM = request.DistanceKM;
        category.MaxParticipants = request.MaxParticipants;
        category.EntryFee = request.EntryFee;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(category));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var organizerID = GetAuthenticatedUserID();
        if (organizerID is null)
        {
            return Unauthorized();
        }

        var category = await dbContext.Categories
            .Include(categoryItem => categoryItem.Event)
            .FirstOrDefaultAsync(categoryItem => categoryItem.CategoryID == id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        if (category.Event.OrganizerID != organizerID.Value)
        {
            return Forbid();
        }

        dbContext.Categories.Remove(category);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Category -> Entry is restricted. Keep dependent entries intact and
            // report a conflict instead of changing the relationship behavior.
            return Conflict(new { message = "The category cannot be deleted while related entries exist." });
        }

        return NoContent();
    }

    private int? GetAuthenticatedUserID()
    {
        var userIDClaim = User?.FindFirstValue("UserID");
        return int.TryParse(userIDClaim, out var userID) && userID > 0
            ? userID
            : null;
    }

    private static bool IsValid(
        string categoryName,
        decimal? distanceKM,
        int? maxParticipants,
        decimal entryFee) =>
        !string.IsNullOrWhiteSpace(categoryName)
        && (!distanceKM.HasValue || distanceKM.Value > 0)
        && (!maxParticipants.HasValue || maxParticipants.Value > 0)
        && entryFee >= 0;

    private ActionResult InvalidCategoryData()
    {
        ModelState.AddModelError(string.Empty, "The category details are invalid.");
        return ValidationProblem(ModelState);
    }

    private static CategoryResponse ToResponse(Category category) => new()
    {
        CategoryID = category.CategoryID,
        EventID = category.EventID,
        CategoryName = category.CategoryName,
        DistanceKM = category.DistanceKM,
        MaxParticipants = category.MaxParticipants,
        EntryFee = category.EntryFee
    };
}
