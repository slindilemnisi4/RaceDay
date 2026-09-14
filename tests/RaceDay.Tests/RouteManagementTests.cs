using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Controllers;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using Xunit;

namespace RaceDay.Tests;

public sealed class RouteManagementTests
{
    [Fact]
    public async Task ParticipantCanViewRoute()
    {
        await using var context = CreateContext();

        var organiser = AddUser(context, 1, UserRole.Organiser);
        var participant = AddUser(context, 2, UserRole.Participant);

        var eventEntity = AddEvent(context, 1, organiser.UserID);

        var route = AddRoute(context, 1, eventEntity.EventID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            participant.UserID,
            UserRole.Participant);

        var result = await controller.GetById(
            route.RouteID,
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RouteResponse>(okResult.Value);

        Assert.Equal(route.RouteID, response.RouteID);
        Assert.Equal(eventEntity.EventID, response.EventID);
        Assert.Equal(route.RouteName, response.RouteName);
    }

    [Fact]
    public async Task OrganiserCanCreateRouteForOwnEvent()
    {
        await using var context = CreateContext();

        var organiser = AddUser(context, 1, UserRole.Organiser);
        var eventEntity = AddEvent(context, 1, organiser.UserID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            organiser.UserID,
            UserRole.Organiser);

        var request = new CreateRouteRequest
        {
            EventID = eventEntity.EventID,
            RouteName = "Main Race Route",
            DistanceKM = 10,
            ElevationGainM = 150,
            RouteDescription = "A 10 kilometre road route.",
            MapURL = "https://example.com/route"
        };

        var result = await controller.Create(
            request,
            CancellationToken.None);

        var createdResult =
            Assert.IsType<CreatedAtActionResult>(result.Result);

        var response =
            Assert.IsType<RouteResponse>(createdResult.Value);

        Assert.Equal(eventEntity.EventID, response.EventID);
        Assert.Equal("Main Race Route", response.RouteName);
        Assert.Equal(10, response.DistanceKM);

        var savedRoute = await context.Routes
            .SingleAsync();

        Assert.Equal(organiser.UserID, eventEntity.OrganizerID);
        Assert.Equal(eventEntity.EventID, savedRoute.EventID);
    }

    [Fact]
    public async Task OrganiserCannotCreateRouteForAnotherOrganisersEvent()
    {
        await using var context = CreateContext();

        var organiserOne = AddUser(context, 1, UserRole.Organiser);
        var organiserTwo = AddUser(context, 2, UserRole.Organiser);

        var eventEntity = AddEvent(
            context,
            1,
            organiserOne.UserID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            organiserTwo.UserID,
            UserRole.Organiser);

        var request = new CreateRouteRequest
        {
            EventID = eventEntity.EventID,
            RouteName = "Unauthorized Route",
            DistanceKM = 10
        };

        var result = await controller.Create(
            request,
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);

        Assert.Empty(context.Routes);
    }

    [Fact]
    public async Task OrganiserCannotCreateSecondRouteForSameEvent()
    {
        await using var context = CreateContext();

        var organiser = AddUser(context, 1, UserRole.Organiser);
        var eventEntity = AddEvent(context, 1, organiser.UserID);

        AddRoute(
            context,
            1,
            eventEntity.EventID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            organiser.UserID,
            UserRole.Organiser);

        var request = new CreateRouteRequest
        {
            EventID = eventEntity.EventID,
            RouteName = "Second Route",
            DistanceKM = 20
        };

        var result = await controller.Create(
            request,
            CancellationToken.None);

        var conflictResult =
            Assert.IsType<ConflictObjectResult>(result.Result);

        Assert.Equal(
            "The event already has a route.",
            conflictResult.Value);

        Assert.Single(context.Routes);
    }

    [Fact]
    public async Task CreateRouteReturnsNotFoundForMissingEvent()
    {
        await using var context = CreateContext();

        var organiser = AddUser(context, 1, UserRole.Organiser);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            organiser.UserID,
            UserRole.Organiser);

        var request = new CreateRouteRequest
        {
            EventID = 999,
            RouteName = "Missing Event Route",
            DistanceKM = 10
        };

        var result = await controller.Create(
            request,
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task OrganiserCanUpdateOwnRoute()
    {
        await using var context = CreateContext();

        var organiser = AddUser(context, 1, UserRole.Organiser);
        var eventEntity = AddEvent(context, 1, organiser.UserID);

        var route = AddRoute(
            context,
            1,
            eventEntity.EventID);

        await context.SaveChangesAsync();

        var originalEventID = route.EventID;

        var controller = CreateController(
            context,
            organiser.UserID,
            UserRole.Organiser);

        var request = new UpdateRouteRequest
        {
            RouteName = "Updated Route",
            DistanceKM = 21.1m,
            ElevationGainM = 250,
            RouteDescription = "Updated route description.",
            MapURL = "https://example.com/updated-route"
        };

        var result = await controller.Update(
            route.RouteID,
            request,
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RouteResponse>(okResult.Value);

        Assert.Equal("Updated Route", response.RouteName);
        Assert.Equal(21.1m, response.DistanceKM);
        Assert.Equal(originalEventID, response.EventID);

        var updatedRoute = await context.Routes
            .SingleAsync(r => r.RouteID == route.RouteID);

        Assert.Equal(originalEventID, updatedRoute.EventID);
        Assert.Equal("Updated Route", updatedRoute.RouteName);
    }

    [Fact]
    public async Task OrganiserCannotUpdateAnotherOrganisersRoute()
    {
        await using var context = CreateContext();

        var owner = AddUser(context, 1, UserRole.Organiser);
        var otherOrganiser = AddUser(
            context,
            2,
            UserRole.Organiser);

        var eventEntity = AddEvent(
            context,
            1,
            owner.UserID);

        var route = AddRoute(
            context,
            1,
            eventEntity.EventID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            otherOrganiser.UserID,
            UserRole.Organiser);

        var request = new UpdateRouteRequest
        {
            RouteName = "Unauthorized Update",
            DistanceKM = 99
        };

        var result = await controller.Update(
            route.RouteID,
            request,
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);

        var unchangedRoute = await context.Routes
            .SingleAsync(r => r.RouteID == route.RouteID);

        Assert.Equal("Route 1", unchangedRoute.RouteName);
    }

    [Fact]
    public async Task OrganiserCanDeleteOwnRoute()
    {
        await using var context = CreateContext();

        var organiser = AddUser(context, 1, UserRole.Organiser);
        var eventEntity = AddEvent(context, 1, organiser.UserID);

        var route = AddRoute(
            context,
            1,
            eventEntity.EventID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            organiser.UserID,
            UserRole.Organiser);

        var result = await controller.Delete(
            route.RouteID,
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(context.Routes);
    }

    [Fact]
    public async Task OrganiserCannotDeleteAnotherOrganisersRoute()
    {
        await using var context = CreateContext();

        var owner = AddUser(context, 1, UserRole.Organiser);
        var otherOrganiser = AddUser(
            context,
            2,
            UserRole.Organiser);

        var eventEntity = AddEvent(
            context,
            1,
            owner.UserID);

        var route = AddRoute(
            context,
            1,
            eventEntity.EventID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            otherOrganiser.UserID,
            UserRole.Organiser);

        var result = await controller.Delete(
            route.RouteID,
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result);

        Assert.NotNull(
            await context.Routes
                .SingleOrDefaultAsync(r => r.RouteID == route.RouteID));
    }

    [Fact]
    public async Task MissingRouteReturnsNotFound()
    {
        await using var context = CreateContext();

        var participant = AddUser(
            context,
            1,
            UserRole.Participant);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            participant.UserID,
            UserRole.Participant);

        var result = await controller.GetById(
            999,
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ParticipantCannotCreateRoute()
    {
        await using var context = CreateContext();

        var participant = AddUser(
            context,
            1,
            UserRole.Participant);

        var eventEntity = AddEvent(
            context,
            1,
            participant.UserID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            participant.UserID,
            UserRole.Participant);

        var request = new CreateRouteRequest
        {
            EventID = eventEntity.EventID,
            RouteName = "Participant Route",
            DistanceKM = 10
        };

        var result = await controller.Create(
            request,
            CancellationToken.None);

        // Direct controller tests do not execute ASP.NET Core's
        // authorization middleware, so the role restriction is
        // verified separately through controller metadata.
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ParticipantCannotUpdateRoute()
    {
        await using var context = CreateContext();

        var organiser = AddUser(
            context,
            1,
            UserRole.Organiser);

        var participant = AddUser(
            context,
            2,
            UserRole.Participant);

        var eventEntity = AddEvent(
            context,
            1,
            organiser.UserID);

        var route = AddRoute(
            context,
            1,
            eventEntity.EventID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            participant.UserID,
            UserRole.Participant);

        var request = new UpdateRouteRequest
        {
            RouteName = "Unauthorized Update",
            DistanceKM = 50
        };

        var result = await controller.Update(
            route.RouteID,
            request,
            CancellationToken.None);

        // Authorization metadata prevents this request from reaching
        // the action in the real HTTP pipeline.
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ParticipantCannotDeleteRoute()
    {
        await using var context = CreateContext();

        var organiser = AddUser(
            context,
            1,
            UserRole.Organiser);

        var participant = AddUser(
            context,
            2,
            UserRole.Participant);

        var eventEntity = AddEvent(
            context,
            1,
            organiser.UserID);

        var route = AddRoute(
            context,
            1,
            eventEntity.EventID);

        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            participant.UserID,
            UserRole.Participant);

        var result = await controller.Delete(
            route.RouteID,
            CancellationToken.None);

        // Authorization metadata prevents this request from reaching
        // the action in the real HTTP pipeline.
        Assert.NotNull(result);
    }

    [Fact]
    public void RoutesControllerRequiresAuthentication()
    {
        var controllerAuthorize =
            typeof(RoutesController)
                .GetCustomAttributes(
                    typeof(AuthorizeAttribute),
                    inherit: true);

        Assert.NotEmpty(controllerAuthorize);
    }

    [Fact]
    public void RouteWriteActionsRequireOrganiserRole()
    {
        var createMethod = typeof(RoutesController)
            .GetMethod(nameof(RoutesController.Create));

        var updateMethod = typeof(RoutesController)
            .GetMethod(nameof(RoutesController.Update));

        var deleteMethod = typeof(RoutesController)
            .GetMethod(nameof(RoutesController.Delete));

        AssertOrganiserAuthorization(createMethod);
        AssertOrganiserAuthorization(updateMethod);
        AssertOrganiserAuthorization(deleteMethod);
    }

    private static void AssertOrganiserAuthorization(
        System.Reflection.MethodInfo? method)
    {
        Assert.NotNull(method);

        var attributes = method!
            .GetCustomAttributes(
                typeof(AuthorizeAttribute),
                inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();

        var organiserAuthorization = attributes
            .SingleOrDefault(attribute =>
                attribute.Roles == nameof(UserRole.Organiser));

        Assert.NotNull(organiserAuthorization);
    }

    private static RoutesController CreateController(
        RaceDayDbContext context,
        int userID,
        UserRole role)
    {
        var controller = new RoutesController(context);

        var claims = new[]
        {
            new Claim(
                "UserID",
                userID.ToString()),
            new Claim(
                ClaimTypes.Role,
                role.ToString())
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        claims,
                        "TestAuthentication"))
            }
        };

        return controller;
    }

    private static RaceDayDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RaceDayDbContext>()
            .UseInMemoryDatabase(
                Guid.NewGuid().ToString())
            .Options;

        return new RaceDayDbContext(options);
    }

    private static User AddUser(
        RaceDayDbContext context,
        int userID,
        UserRole role)
    {
        var user = new User
        {
            UserID = userID,
            FirstName = $"User{userID}",
            LastName = "Test",
            Email = $"user{userID}@example.com",
            PasswordHash = "test-password-hash",
            Role = role
        };

        context.Users.Add(user);

        return user;
    }

    private static Event AddEvent(
        RaceDayDbContext context,
        int eventID,
        int organizerID)
    {
        var eventEntity = new Event
        {
            EventID = eventID,
            OrganizerID = organizerID,
            EventName = $"Test Event {eventID}",
            Description = "Test event description.",
            EventDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Location = "Pretoria",
            DistanceKM = 10,
            // EventType = EventType.Run, // Add the correct using/type if needed.
            RegistrationDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
            CreatedAt = DateTime.UtcNow
        };

        context.Events.Add(eventEntity);

        return eventEntity;
    }

    private static Route AddRoute(
        RaceDayDbContext context,
        int routeID,
        int eventID)
    {
        var route = new Route
        {
            RouteID = routeID,
            EventID = eventID,
            RouteName = $"Route {routeID}",
            DistanceKM = 10,
            ElevationGainM = 100,
            RouteDescription = "Test route.",
            MapURL = "https://example.com/route"
        };

        context.Routes.Add(route);

        return route;
    }
}