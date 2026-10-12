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

        var result = await fixture.Controller.Create(fixture.ValidRequest(entryFee: 42.50m), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var response = Assert.IsType<EventResponse>(created.Value);
        var persistedEvent = await fixture.DbContext.Events.SingleAsync();
        var defaultCategory = await fixture.DbContext.Categories.SingleAsync();

        Assert.Equal(fixture.User.UserID, response.OrganizerID);
        Assert.Equal(fixture.User.UserID, persistedEvent.OrganizerID);
        Assert.Equal("Spring Race", persistedEvent.EventName);
        Assert.Equal(42.50m, response.EntryFee);
        Assert.Equal(persistedEvent.EventID, defaultCategory.EventID);
        Assert.Equal("General Entry", defaultCategory.CategoryName);
        Assert.Equal(42.50m, defaultCategory.EntryFee);
        Assert.Equal(EventStatus.Active, persistedEvent.Status);
    }

    [Fact]
    public async Task FreeEventCreatesGeneralEntryWithZeroFee()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Create(fixture.ValidRequest(), CancellationToken.None);

        var response = Assert.IsType<EventResponse>(Assert.IsType<CreatedResult>(result.Result).Value);
        var category = await fixture.DbContext.Categories.SingleAsync();
        Assert.Equal(0m, response.EntryFee);
        Assert.Equal(0m, category.EntryFee);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.001)]
    [InlineData(100000000)]
    public async Task InvalidEntryFeeIsRejected(decimal entryFee)
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Create(
            fixture.ValidRequest(entryFee),
            CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        Assert.Empty(await fixture.DbContext.Events.ToListAsync());
        Assert.Empty(await fixture.DbContext.Categories.ToListAsync());
    }

    [Fact]
    public async Task ParticipantIsNotAllowedByAuthorizationAttribute()
    {
        var authorize = typeof(EventsController)
            .GetMethod(nameof(EventsController.Create))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Organiser), authorize.Roles);
    }

    [Fact]
    public void EventsControllerRequiresAuthentication()
    {
        var controllerAuthorize = typeof(EventsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();
        var authorize = typeof(EventsController)
            .GetMethod(nameof(EventsController.Create))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Null(typeof(EventsController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
            .SingleOrDefault());
        Assert.Null(controllerAuthorize.Roles);
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

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task InvalidCreateRequestIsRejected(CreateEventRequest request)
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Create(request, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }

    [Fact]
    public async Task ValidCreateRequestRemainsAccepted()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Create(fixture.ValidRequest(), CancellationToken.None);

        Assert.IsType<CreatedResult>(result.Result);
    }

    [Fact]
    public async Task GetMine_ReturnsOnlyAuthenticatedOrganisersEventsAndEntryCounts()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var otherOrganiser = new User
        {
            FirstName = "Other",
            LastName = "Organiser",
            Email = "OTHER@EXAMPLE.COM",
            PasswordHash = "hashed-password",
            Role = UserRole.Organiser,
            CreatedAt = DateTime.UtcNow
        };
        var ownedEvent = new Event
        {
            OrganizerID = fixture.User.UserID,
            EventName = "Owned event",
            Description = "Owned",
            EventDate = new DateOnly(2026, 5, 10),
            Location = "Park",
            DistanceKM = 10,
            EventType = EventTypeOption.Run,
            RegistrationDeadline = new DateOnly(2026, 5, 1),
            CreatedAt = DateTime.UtcNow,
            Categories =
            [
                new Category
                {
                    CategoryName = "Open",
                    EntryFee = 0,
                    Entries =
                    [
                        new Entry { UserID = fixture.User.UserID, EntryDate = DateTime.UtcNow }
                    ]
                }
            ]
        };
        var otherEvent = new Event
        {
            Organizer = otherOrganiser,
            EventName = "Other event",
            Description = "Not owned",
            EventDate = new DateOnly(2026, 6, 10),
            Location = "Other Park",
            DistanceKM = 5,
            EventType = EventTypeOption.Walk,
            RegistrationDeadline = new DateOnly(2026, 6, 1),
            CreatedAt = DateTime.UtcNow
        };
        fixture.DbContext.Events.AddRange(ownedEvent, otherEvent);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Controller.GetMine(CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var events = Assert.IsAssignableFrom<IReadOnlyList<OrganiserEventSummaryResponse>>(response.Value);
        var singleEvent = Assert.Single(events);
        Assert.Equal("Owned event", singleEvent.EventName);
        Assert.Equal(1, singleEvent.TotalEnrolments);
    }

    [Fact]
    public void GetMine_RequiresOrganiserRole()
    {
        var authorize = typeof(EventsController)
            .GetMethod(nameof(EventsController.GetMine))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Organiser), authorize.Roles);
    }

    public static IEnumerable<object[]> InvalidCreateRequests() =>
        new[]
        {
            new object[] { new CreateEventRequest { EventName = "", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "   ", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "Description", EventDate = default, Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = default } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 0, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = -1, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = null, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = (EventTypeOption)99, RegistrationDeadline = new DateOnly(2026, 5, 1) } },
            new object[] { new CreateEventRequest { EventName = "Name", Description = "Description", EventDate = new DateOnly(2026, 5, 10), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 5, 11) } }
        };

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

        public CreateEventRequest ValidRequest(decimal entryFee = 0m) => new()
        {
            EventName = "Spring Race",
            Description = "A local running event.",
            EventDate = new DateOnly(2026, 5, 10),
            Location = "RaceDay Park",
            DistanceKM = 10,
            EntryFee = entryFee,
            EventType = EventTypeOption.Run,
            RegistrationDeadline = new DateOnly(2026, 5, 1)
        };

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
