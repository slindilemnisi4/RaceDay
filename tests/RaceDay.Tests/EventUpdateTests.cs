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

public sealed class EventUpdateTests
{
    [Fact]
    public async Task OrganiserCanUpdateOwnEventAndProtectedFieldsRemainUnchanged()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var original = fixture.AddEvent();

        var result = await fixture.Controller.Update(
            original.EventID,
            fixture.ValidRequest(),
            CancellationToken.None);

        var response = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var updated = await fixture.DbContext.Events.SingleAsync();

        Assert.Equal(original.EventID, updated.EventID);
        Assert.Equal(original.OrganizerID, updated.OrganizerID);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.Equal("Updated Event", updated.EventName);
        Assert.Equal("Updated Event", response.EventName);
    }

    [Fact]
    public async Task OrganiserCannotUpdateAnotherOrganisersEvent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var otherOrganizer = new User
        {
            FirstName = "Other",
            LastName = "Organizer",
            Email = "OTHER@EXAMPLE.COM",
            PasswordHash = "hash",
            Role = UserRole.Organiser,
            CreatedAt = DateTime.UtcNow
        };
        fixture.DbContext.Users.Add(otherOrganizer);
        await fixture.DbContext.SaveChangesAsync();
        var otherEvent = fixture.AddEventFor(otherOrganizer.UserID);

        var result = await fixture.Controller.Update(
            otherEvent.EventID,
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal("Original Event", (await fixture.DbContext.Events.SingleAsync()).EventName);
    }

    [Fact]
    public async Task MissingEventReturnsNotFound()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Update(
            999,
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UnauthenticatedRequestReturnsUnauthorized()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser, authenticated: false);

        var result = await fixture.Controller.Update(
            1,
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void UpdateRequiresOrganiserAuthorization()
    {
        var authorize = typeof(EventsController)
            .GetMethod(nameof(EventsController.Update))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Organiser), authorize.Roles);
    }

    [Fact]
    public void UpdateRequestDoesNotExposeProtectedFields()
    {
        var propertyNames = typeof(UpdateEventRequest).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain("EventID", propertyNames);
        Assert.DoesNotContain("OrganizerID", propertyNames);
        Assert.DoesNotContain("CreatedAt", propertyNames);
        Assert.DoesNotContain("Categories", propertyNames);
        Assert.DoesNotContain("Route", propertyNames);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidUpdateDataIsRejected(UpdateEventRequest request)
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent();

        var result = await fixture.Controller.Update(
            eventEntity.EventID,
            request,
            CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }

    public static IEnumerable<object[]> InvalidRequests() =>
        new[]
        {
            new object[] { new UpdateEventRequest { EventName = "", Description = "Valid", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Valid", Description = "Valid", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 0, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Valid", Description = "Valid", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = (EventTypeOption)99, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Valid", Description = "Valid", EventDate = new DateOnly(2026, 5, 1), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 10) } }
        };

    [Theory]
    [MemberData(nameof(InvalidUpdateRequests))]
    public async Task AllInvalidUpdateFieldsAreRejected(UpdateEventRequest request)
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent();

        var result = await fixture.Controller.Update(
            eventEntity.EventID,
            request,
            CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }

    [Fact]
    public async Task ValidUpdateRequestRemainsAccepted()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent();

        var result = await fixture.Controller.Update(
            eventEntity.EventID,
            fixture.ValidRequest(),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    public static IEnumerable<object[]> InvalidUpdateRequests() =>
        new[]
        {
            new object[] { new UpdateEventRequest { EventName = "", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Name", Description = "", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Name", Description = "Description", EventDate = default, Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 0, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = -1, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = null, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = (EventTypeOption)99, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new UpdateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 11) } }
        };

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
                PasswordHash = "hash",
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

        public Event AddEvent() => AddEventFor(User.UserID);

        public Event AddEventFor(int organizerID)
        {
            var eventEntity = new Event
            {
                OrganizerID = organizerID,
                EventName = "Original Event",
                Description = "Original description",
                EventDate = new DateOnly(2026, 5, 10),
                Location = "Original location",
                DistanceKM = 10,
                EventType = EventTypeOption.Run,
                RegistrationDeadline = new DateOnly(2026, 5, 1),
                CreatedAt = DateTime.UtcNow
            };
            DbContext.Events.Add(eventEntity);
            DbContext.SaveChanges();
            return eventEntity;
        }

        public UpdateEventRequest ValidRequest() => new()
        {
            EventName = "Updated Event",
            Description = "Updated description",
            EventDate = new DateOnly(2026, 6, 10),
            Location = "Updated location",
            DistanceKM = 15,
            EventType = EventTypeOption.Cycle,
            RegistrationDeadline = new DateOnly(2026, 6, 1)
        };

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
