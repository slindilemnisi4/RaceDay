using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Controllers;
using RaceDay.API.Data;
using RaceDay.API.Models;
using Xunit;

namespace RaceDay.Tests;

public sealed class EventDeletionTests
{
    [Fact]
    public async Task OrganiserCanDeleteOwnEventAndReturnsNoContent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent();

        var result = await fixture.Controller.Delete(eventEntity.EventID, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await fixture.DbContext.Events.ToListAsync());
    }

    [Fact]
    public async Task OrganiserCannotDeleteAnotherOrganisersEvent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var otherOrganizer = fixture.AddUser(UserRole.Organiser, "OTHER@EXAMPLE.COM");
        var otherEvent = fixture.AddEvent(otherOrganizer.UserID);

        var result = await fixture.Controller.Delete(otherEvent.EventID, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.NotNull(await fixture.DbContext.Events.FindAsync(otherEvent.EventID));
    }

    [Fact]
    public async Task MissingEventReturnsNotFound()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Delete(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UnauthenticatedRequestReturnsUnauthorized()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser, authenticated: false);

        var result = await fixture.Controller.Delete(1, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public void DeleteRequiresOrganiserAuthorization()
    {
        var authorize = typeof(EventsController)
            .GetMethod(nameof(EventsController.Delete))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Organiser), authorize.Roles);
    }

    [Fact]
    public void EventRelationshipsRetainRestrictedDeleteBehavior()
    {
        using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventType = fixture.DbContext.Model.FindEntityType(typeof(Event))!;
        var categoryForeignKey = fixture.DbContext.Model.FindEntityType(typeof(Category))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Event));
        var routeForeignKey = fixture.DbContext.Model.FindEntityType(typeof(Route))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Event));

        Assert.Equal(DeleteBehavior.Restrict, categoryForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, routeForeignKey.DeleteBehavior);
        Assert.NotNull(eventType);
    }

    private sealed class TestFixture : IDisposable, IAsyncDisposable
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

        public User AddUser(UserRole role, string email)
        {
            var user = new User
            {
                FirstName = "Other",
                LastName = "Organizer",
                Email = email,
                PasswordHash = "hash",
                Role = role,
                CreatedAt = DateTime.UtcNow
            };
            DbContext.Users.Add(user);
            DbContext.SaveChanges();
            return user;
        }

        public Event AddEvent(int? organizerID = null)
        {
            var eventEntity = new Event
            {
                OrganizerID = organizerID ?? User.UserID,
                EventName = "Event to Delete",
                Description = "An event for deletion tests.",
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

        public void Dispose() => DbContext.Dispose();

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
