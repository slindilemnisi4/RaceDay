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

public sealed class EnrolmentCreationTests
{
    [Fact]
    public async Task ParticipantCanCreateEnrolmentUsingAuthenticatedUserID()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent(new DateOnly(2026, 12, 31));
        var category = fixture.AddCategory(eventEntity.EventID, maxParticipants: 10);

        var result = await fixture.Controller.Create(
            eventEntity.EventID,
            new CreateEnrolmentRequest { CategoryID = category.CategoryID },
            CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var response = Assert.IsType<EnrolmentResponse>(created.Value);
        var entry = await fixture.DbContext.Entries.SingleAsync();
        Assert.Equal(fixture.User.UserID, entry.UserID);
        Assert.Equal(category.CategoryID, entry.CategoryID);
        Assert.Equal(entry.EntryID, response.EntryID);
        Assert.InRange(entry.EntryDate, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.Empty(await fixture.DbContext.Results.ToListAsync());
    }

    [Fact]
    public async Task ParticipantCannotSupplyAnotherUserID()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent(new DateOnly(2026, 12, 31));
        var category = fixture.AddCategory(eventEntity.EventID, maxParticipants: 10);

        var result = await fixture.Controller.Create(
            eventEntity.EventID,
            new CreateEnrolmentRequest { CategoryID = category.CategoryID },
            CancellationToken.None);

        var entry = await fixture.DbContext.Entries.SingleAsync();
        Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(fixture.User.UserID, entry.UserID);
        Assert.DoesNotContain("UserID", typeof(CreateEnrolmentRequest).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void OrganiserCannotCreateParticipantEnrolmentThroughAuthorizationMetadata()
    {
        var authorize = typeof(EnrolmentsController)
            .GetMethod(nameof(EnrolmentsController.Create))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Participant), authorize.Roles);
    }

    [Fact]
    public void CreationRequiresParticipantRoleMetadata()
    {
        var authorize = typeof(EnrolmentsController)
            .GetMethod(nameof(EnrolmentsController.Create))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Participant), authorize.Roles);
    }

    [Fact]
    public async Task UnauthenticatedCreationReturnsUnauthorized()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant, authenticated: false);

        var result = await fixture.Controller.Create(
            1,
            new CreateEnrolmentRequest { CategoryID = 1 },
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task MissingCategoryOrMismatchedEventReturnsNotFound()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent(new DateOnly(2026, 12, 31));
        var category = fixture.AddCategory(eventEntity.EventID, maxParticipants: 10);

        var missing = await fixture.Controller.Create(
            eventEntity.EventID,
            new CreateEnrolmentRequest { CategoryID = 999 },
            CancellationToken.None);
        var mismatched = await fixture.Controller.Create(
            eventEntity.EventID + 1,
            new CreateEnrolmentRequest { CategoryID = category.CategoryID },
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(missing.Result);
        Assert.IsType<NotFoundResult>(mismatched.Result);
    }

    [Fact]
    public async Task DuplicateEnrolmentReturnsConflict()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent(new DateOnly(2026, 12, 31));
        var category = fixture.AddCategory(eventEntity.EventID, maxParticipants: 10);
        fixture.AddEntry(fixture.User.UserID, category.CategoryID);

        var result = await fixture.Controller.Create(
            eventEntity.EventID,
            new CreateEnrolmentRequest { CategoryID = category.CategoryID },
            CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task ClosedRegistrationReturnsConflict()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        var category = fixture.AddCategory(eventEntity.EventID, maxParticipants: 10);

        var result = await fixture.Controller.Create(
            eventEntity.EventID,
            new CreateEnrolmentRequest { CategoryID = category.CategoryID },
            CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task FullCategoryReturnsConflict()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent(new DateOnly(2026, 12, 31));
        var category = fixture.AddCategory(eventEntity.EventID, maxParticipants: 1);
        fixture.AddEntry(fixture.OtherParticipant.UserID, category.CategoryID);

        var result = await fixture.Controller.Create(
            eventEntity.EventID,
            new CreateEnrolmentRequest { CategoryID = category.CategoryID },
            CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(result.Result).StatusCode);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(RaceDayDbContext dbContext, EnrolmentsController controller, User user, User otherParticipant)
        {
            DbContext = dbContext;
            Controller = controller;
            User = user;
            OtherParticipant = otherParticipant;
        }

        public RaceDayDbContext DbContext { get; }
        public EnrolmentsController Controller { get; }
        public User User { get; }
        public User OtherParticipant { get; }

        public static TestFixture Create(UserRole role, bool authenticated = true)
        {
            var options = new DbContextOptionsBuilder<RaceDayDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new RaceDayDbContext(options);
            var user = new User { FirstName = "Test", LastName = "User", Email = "USER@EXAMPLE.COM", PasswordHash = "hash", Role = role, CreatedAt = DateTime.UtcNow };
            var otherParticipant = new User { FirstName = "Other", LastName = "Participant", Email = "OTHER@EXAMPLE.COM", PasswordHash = "hash", Role = UserRole.Participant, CreatedAt = DateTime.UtcNow };
            dbContext.Users.AddRange(user, otherParticipant);
            dbContext.SaveChanges();

            var controller = new EnrolmentsController(dbContext);
            if (authenticated)
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            new[] { new Claim("UserID", user.UserID.ToString()), new Claim("Role", role.ToString()) },
                            "TestAuth"))
                    }
                };
            }

            return new TestFixture(dbContext, controller, user, otherParticipant);
        }

        public Event AddEvent(DateOnly registrationDeadline)
        {
            var eventEntity = new Event
            {
                OrganizerID = User.UserID,
                EventName = "Test Event",
                Description = "Test event",
                EventDate = registrationDeadline.AddDays(10),
                Location = "Park",
                DistanceKM = 10,
                EventType = EventTypeOption.Run,
                RegistrationDeadline = registrationDeadline,
                CreatedAt = DateTime.UtcNow
            };
            DbContext.Events.Add(eventEntity);
            DbContext.SaveChanges();
            return eventEntity;
        }

        public Category AddCategory(int eventID, int? maxParticipants)
        {
            var category = new Category
            {
                EventID = eventID,
                CategoryName = "10K",
                DistanceKM = 10,
                MaxParticipants = maxParticipants,
                EntryFee = 25
            };
            DbContext.Categories.Add(category);
            DbContext.SaveChanges();
            return category;
        }

        public void AddEntry(int userID, int categoryID)
        {
            DbContext.Entries.Add(new Entry { UserID = userID, CategoryID = categoryID, EntryDate = DateTime.UtcNow });
            DbContext.SaveChanges();
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
