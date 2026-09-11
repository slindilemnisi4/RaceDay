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

public sealed class EnrolmentViewingTests
{
    [Fact]
    public async Task ParticipantCanViewOnlyTheirOwnEnrolments()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent(fixture.Organizer.UserID);
        var category = fixture.AddCategory(eventEntity.EventID);
        fixture.AddEntry(fixture.User.UserID, category.CategoryID);
        fixture.AddEntry(fixture.OtherParticipant.UserID, category.CategoryID);

        var result = await fixture.Controller.GetMine(CancellationToken.None);

        var enrolments = Assert.IsType<List<EnrolmentResponse>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(enrolments);
        Assert.Equal(category.CategoryID, enrolments[0].CategoryID);
    }

    [Fact]
    public async Task ParticipantWithNoEnrolmentsReceivesEmptyList()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);

        var result = await fixture.Controller.GetMine(CancellationToken.None);

        var enrolments = Assert.IsType<List<EnrolmentResponse>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(enrolments);
    }

    [Fact]
    public async Task UnauthenticatedRequestsAreRejected()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant, authenticated: false);

        var mine = await fixture.Controller.GetMine(CancellationToken.None);
        var eventEnrolments = await fixture.Controller.GetForEvent(1, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(mine.Result);
        Assert.IsType<UnauthorizedResult>(eventEnrolments.Result);
    }

    [Fact]
    public async Task OrganiserCanViewEnrolmentsForTheirEventThroughCategoryEventRelationship()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent(fixture.User.UserID);
        var category = fixture.AddCategory(eventEntity.EventID);
        fixture.AddEntry(fixture.OtherParticipant.UserID, category.CategoryID);

        var result = await fixture.Controller.GetForEvent(eventEntity.EventID, CancellationToken.None);

        var enrolments = Assert.IsType<List<EnrolmentResponse>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(enrolments);
        Assert.Equal(eventEntity.EventID, enrolments[0].EventID);
        Assert.Equal("Test Event", enrolments[0].EventName);
    }

    [Fact]
    public async Task OrganiserCannotViewAnotherOrganisersEventEnrolments()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var otherOrganizer = fixture.AddUser(UserRole.Organiser, "OTHER@EXAMPLE.COM");
        var eventEntity = fixture.AddEvent(otherOrganizer.UserID);

        var result = await fixture.Controller.GetForEvent(eventEntity.EventID, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task MissingEventReturnsNotFoundForOrganiserView()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.GetForEvent(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void AuthorizationMetadataMatchesThePlannedEndpoints()
    {
        var controllerAuthorization = typeof(EnrolmentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();
        Assert.Null(controllerAuthorization.Roles);

        var participantAuthorization = typeof(EnrolmentsController)
            .GetMethod(nameof(EnrolmentsController.GetMine))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();
        var organiserAuthorization = typeof(EnrolmentsController)
            .GetMethod(nameof(EnrolmentsController.GetForEvent))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Participant), participantAuthorization.Roles);
        Assert.Equal(nameof(UserRole.Organiser), organiserAuthorization.Roles);
    }

    [Fact]
    public void EnrolmentResponseDoesNotExposeSensitiveOrNavigationData()
    {
        var propertyNames = typeof(EnrolmentResponse).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain("PasswordHash", propertyNames);
        Assert.DoesNotContain("User", propertyNames);
        Assert.DoesNotContain("Category", propertyNames);
        Assert.DoesNotContain("Event", propertyNames);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(
            RaceDayDbContext dbContext,
            EnrolmentsController controller,
            User user,
            User organizer,
            User otherParticipant)
        {
            DbContext = dbContext;
            Controller = controller;
            User = user;
            Organizer = organizer;
            OtherParticipant = otherParticipant;
        }

        public RaceDayDbContext DbContext { get; }
        public EnrolmentsController Controller { get; }
        public User User { get; }
        public User Organizer { get; }
        public User OtherParticipant { get; }

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
            var organizer = new User
            {
                FirstName = "Organizer",
                LastName = "User",
                Email = "ORGANIZER@EXAMPLE.COM",
                PasswordHash = "hash",
                Role = UserRole.Organiser,
                CreatedAt = DateTime.UtcNow
            };
            var otherParticipant = new User
            {
                FirstName = "Other",
                LastName = "Participant",
                Email = "OTHER@EXAMPLE.COM",
                PasswordHash = "hash",
                Role = UserRole.Participant,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Users.AddRange(user, organizer, otherParticipant);
            dbContext.SaveChanges();

            var controller = new EnrolmentsController(dbContext);
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
                            "TestAuth"))
                    }
                };
            }

            return new TestFixture(dbContext, controller, user, organizer, otherParticipant);
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

        public Event AddEvent(int organizerID)
        {
            var eventEntity = new Event
            {
                OrganizerID = organizerID,
                EventName = "Test Event",
                Description = "Test event",
                EventDate = new DateOnly(2026, 5, 10),
                Location = "Park",
                DistanceKM = 10,
                EventType = EventTypeOption.Run,
                RegistrationDeadline = new DateOnly(2026, 5, 1),
                CreatedAt = DateTime.UtcNow
            };
            DbContext.Events.Add(eventEntity);
            DbContext.SaveChanges();
            return eventEntity;
        }

        public Category AddCategory(int eventID)
        {
            var category = new Category
            {
                EventID = eventID,
                CategoryName = "10K",
                DistanceKM = 10,
                MaxParticipants = 100,
                EntryFee = 25
            };
            DbContext.Categories.Add(category);
            DbContext.SaveChanges();
            return category;
        }

        public Entry AddEntry(int userID, int categoryID)
        {
            var entry = new Entry
            {
                UserID = userID,
                CategoryID = categoryID,
                EntryDate = DateTime.UtcNow
            };
            DbContext.Entries.Add(entry);
            DbContext.SaveChanges();
            return entry;
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
