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

public sealed class EventCreationTests
{
    [Fact]
    public async Task OrganiserCanCreateEventAndOrganizerIDComesFromJwt()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Create(fixture.ValidRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var response = Assert.IsType<EventResponse>(created.Value);
        var persistedEvent = await fixture.DbContext.Events.SingleAsync();

        Assert.Equal(fixture.User.UserID, response.OrganizerID);
        Assert.Equal(fixture.User.UserID, persistedEvent.OrganizerID);
        Assert.Equal("Spring Race", persistedEvent.EventName);
    }

    [Fact]
    public async Task ParticipantIsNotAllowedByAuthorizationAttribute()
    {
        var authorize = typeof(EventsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Organiser), authorize.Roles);
    }

    [Fact]
    public void EventsControllerRequiresAuthentication()
    {
        var authorize = typeof(EventsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Null(typeof(EventsController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
            .SingleOrDefault());
        Assert.Equal(nameof(UserRole.Organiser), authorize.Roles);
    }

    [Fact]
    public async Task UnauthenticatedControllerRequestReturnsUnauthorized()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser, authenticated: false);

        var result = await fixture.Controller.Create(fixture.ValidRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task InvalidDistanceIsRejected()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var request = new CreateEventRequest
        {
            EventName = "Spring Race",
            Description = "A local running event.",
            EventDate = new DateOnly(2026, 5, 10),
            Location = "RaceDay Park",
            DistanceKM = 0,
            EventType = EventTypeOption.Run,
            RegistrationDeadline = new DateOnly(2026, 5, 1)
        };

        var result = await fixture.Controller.Create(request, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }

    [Fact]
    public async Task RegistrationDeadlineAfterEventDateIsRejected()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var request = new CreateEventRequest
        {
            EventName = "Spring Race",
            Description = "A local running event.",
            Location = "RaceDay Park",
            DistanceKM = 10,
            EventType = EventTypeOption.Run,
            EventDate = new DateOnly(2026, 4, 1),
            RegistrationDeadline = new DateOnly(2026, 4, 2)
        };

        var result = await fixture.Controller.Create(request, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }

    [Fact]
    public void RequestAndResponseDoNotExposeClientControlledOrganizerIDOrSensitiveFields()
    {
        Assert.DoesNotContain("OrganizerID", typeof(CreateEventRequest).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("PasswordHash", typeof(EventResponse).GetProperties().Select(property => property.Name));
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(RaceDayDbContext dbContext, EventsController controller, User user)
        {
            DbContext = dbContext;
            Controller = controller;
            User = user;
        }

        public RaceDayDbContext DbContext { get; }

        public EventsController Controller { get; }

        public User User { get; }

        public static TestFixture Create(UserRole role, bool authenticated = true)
        {
            var options = new DbContextOptionsBuilder<RaceDayDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new RaceDayDbContext(options);
            var user = new User
            {
                FirstName = "Test",
                LastName = "User",
                Email = "USER@EXAMPLE.COM",
                PasswordHash = "hashed-password",
                Role = role,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
            dbContext.SaveChanges();

            var controller = new EventsController(dbContext);
            if (authenticated)
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            new[]
                            {
                                new Claim("UserID", user.UserID.ToString()),
                                new Claim("Role", role.ToString())
                            },
                            authenticationType: "TestAuth"))
                    }
                };
            }

            return new TestFixture(dbContext, controller, user);
        }

        public CreateEventRequest ValidRequest() => new()
        {
            EventName = "Spring Race",
            Description = "A local running event.",
            EventDate = new DateOnly(2026, 5, 10),
            Location = "RaceDay Park",
            DistanceKM = 10,
            EventType = EventTypeOption.Run,
            RegistrationDeadline = new DateOnly(2026, 5, 1)
        };

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
