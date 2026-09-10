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

public sealed class EventViewingTests
{
    [Fact]
    public async Task GetAll_ReturnsAvailableEventsForAuthenticatedOrganiser()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        fixture.AddEvent("Organiser Event");

        var result = await fixture.Controller.GetAll(CancellationToken.None);

        var events = Assert.IsType<List<EventResponse>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(events);
        Assert.Equal("Organiser Event", events[0].EventName);
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyCollectionWhenNoEventsExist()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);

        var result = await fixture.Controller.GetAll(CancellationToken.None);

        var events = Assert.IsType<List<EventResponse>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(events);
    }

    [Fact]
    public async Task GetByID_ReturnsRequestedEventForAuthenticatedParticipant()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent("Viewed Event");

        var result = await fixture.Controller.GetByID(eventEntity.EventID, CancellationToken.None);

        var response = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(eventEntity.EventID, response.EventID);
        Assert.Equal("Viewed Event", response.EventName);
    }

    [Fact]
    public async Task GetByID_ReturnsNotFoundForMissingEvent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.GetByID(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void ViewingControllerRequiresAuthentication()
    {
        var authorize = typeof(EventsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Null(authorize.Roles);
    }

    [Fact]
    public void ViewingRequiresAuthenticationButNotAnOrganiserRole()
    {
        var controllerAuthorization = typeof(EventsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Null(controllerAuthorization.Roles);
        Assert.Null(typeof(EventsController)
            .GetMethod(nameof(EventsController.GetAll))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault());
    }

    [Fact]
    public void EventResponsesDoNotExposeUserSensitiveInformation()
    {
        var propertyNames = typeof(EventResponse).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain("PasswordHash", propertyNames);
        Assert.DoesNotContain("Categories", propertyNames);
        Assert.DoesNotContain("Route", propertyNames);
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

        public Event AddEvent(string name)
        {
            var eventEntity = new Event
            {
                OrganizerID = User.UserID,
                EventName = name,
                Description = "A viewed event.",
                EventDate = new DateOnly(2026, 5, 10),
                Location = "RaceDay Park",
                DistanceKM = 10,
                EventType = EventTypeOption.Run,
                RegistrationDeadline = new DateOnly(2026, 5, 1),
                CreatedAt = DateTime.UtcNow
            };
            DbContext.Events.Add(eventEntity);
            DbContext.SaveChanges();
            return eventEntity;
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
