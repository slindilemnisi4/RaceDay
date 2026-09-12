using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RouteEntity = RaceDay.API.Models.Route;
using RaceDay.API.Models;
using System.Security.Claims;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/routes")]
[Authorize]
public sealed class RoutesController : ControllerBase
{
    private const string UserIdClaimType = "UserID";

    private readonly RaceDayDbContext _context;

    public RoutesController(RaceDayDbContext context)
    {
        _context = context;
    }

    // GET: api/routes/{id}
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RouteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteResponse>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        var route = await _context.Routes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.RouteID == id,
                cancellationToken);

        if (route is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(route));
    }

    // POST: api/routes
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(RouteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RouteResponse>> Create(
        CreateRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizerId(out var organizerId))
        {
            return Unauthorized();
        }

        if (!ValidateRouteName(request.RouteName, out var problem))
        {
            return problem;
        }

        var eventEntity = await _context.Events
            .FirstOrDefaultAsync(
                e => e.EventID == request.EventID,
                cancellationToken);

        if (eventEntity is null)
        {
            return NotFound();
        }

        if (eventEntity.OrganizerID != organizerId)
        {
            return Forbid();
        }

        var hasRoute = await _context.Routes
            .AnyAsync(
                r => r.EventID == request.EventID,
                cancellationToken);

        if (hasRoute)
        {
            return Conflict("The event already has a route.");
        }

        var route = new RouteEntity
        {
            EventID = request.EventID,
            RouteName = request.RouteName.Trim(),
            DistanceKM = request.DistanceKM,
            ElevationGainM = (int?)request.ElevationGainM,   // ⚠️ truncates toward zero
            RouteDescription = request.RouteDescription?.Trim(),
            MapURL = request.MapURL?.Trim()
        };

        _context.Routes.Add(route);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = route.RouteID },
            ToResponse(route));
    }

    // PUT: api/routes/{id}
    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Organiser))]
    [ProducesResponseType(typeof(RouteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RouteResponse>> Update(
        int id,
        UpdateRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizerId(out var organizerId))
        {
            return Unauthorized();
        }

        if (!ValidateRouteName(request.RouteName, out var problem))
        {
            return problem;
        }

        var route = await _context.Routes
            .Include(r => r.Event)
            .FirstOrDefaultAsync(
                r => r.RouteID == id,
                cancellationToken);

        if (route is null)
        {
            return NotFound();
        }

        if (route.Event.OrganizerID != organizerId)
        {
            return Forbid();
        }

        ApplyRoute(request, route);

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(route));
    }

    // DELETE: api/routes/{id}
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
        if (!TryGetOrganizerId(out var organizerId))
        {
            return Unauthorized();
        }

        var route = await _context.Routes
            .Include(r => r.Event)
            .FirstOrDefaultAsync(
                r => r.RouteID == id,
                cancellationToken);

        if (route is null)
        {
            return NotFound();
        }

        if (route.Event.OrganizerID != organizerId)
        {
            return Forbid();
        }

        _context.Routes.Remove(route);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private bool TryGetOrganizerId(out int organizerId)
    {
        var claim = User.FindFirstValue(UserIdClaimType);

        return int.TryParse(claim, out organizerId);
    }

    private bool ValidateRouteName(
        string? routeName,
        out ActionResult problem)
    {
        if (string.IsNullOrWhiteSpace(routeName))
        {
            ModelState.AddModelError(
                nameof(CreateRouteRequest.RouteName),
                "Route name is required.");

            problem = ValidationProblem(ModelState);
            return false;
        }

        problem = null!;
        return true;
    }

    private static void ApplyRoute(
        UpdateRouteRequest request,
        RouteEntity route)
    {
        route.RouteName = request.RouteName.Trim();
        route.DistanceKM = request.DistanceKM;
        route.ElevationGainM = (int?)request.ElevationGainM;
        route.RouteDescription = request.RouteDescription?.Trim();
        route.MapURL = request.MapURL?.Trim();
    }

    private static RouteResponse ToResponse(RouteEntity route)
    {
        return new RouteResponse
        {
            RouteID = route.RouteID,
            EventID = route.EventID,
            RouteName = route.RouteName,
            DistanceKM = route.DistanceKM,
            ElevationGainM = route.ElevationGainM,
            RouteDescription = route.RouteDescription,
            MapURL = route.MapURL
        };
    }
}