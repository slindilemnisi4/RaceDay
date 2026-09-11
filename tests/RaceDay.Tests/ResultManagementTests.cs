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

public sealed class ResultManagementTests
{
    [Fact]
    public async Task OrganiserCanCreateResultForTheirEvent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var entry = fixture.AddEntry(fixture.User.UserID);

        var result = await fixture.Controller.Create(entry.EntryID, fixture.CreateRequest(entry.EntryID), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var response = Assert.IsType<ResultResponse>(created.Value);
        var stored = await fixture.DbContext.Results.SingleAsync();
        Assert.Equal(entry.EntryID, stored.EntryID);
        Assert.Equal(stored.ResultID, response.ResultID);
        Assert.InRange(stored.RecordedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task DuplicateResultReturnsConflict()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var entry = fixture.AddEntry(fixture.User.UserID);
        fixture.AddResult(entry.EntryID);

        var result = await fixture.Controller.Create(entry.EntryID, fixture.CreateRequest(entry.EntryID), CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task OrganiserCannotCreateResultForAnotherOrganisersEvent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var otherOrganizer = fixture.AddUser(UserRole.Organiser, "OTHER@EXAMPLE.COM");
        var otherEvent = fixture.OtherEvent(otherOrganizer.UserID);
        var entry = fixture.AddEntry(otherOrganizer.UserID, otherEvent);

        var result = await fixture.Controller.Create(entry.EntryID, fixture.CreateRequest(entry.EntryID), CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public void ResultWriteActionsRequireOrganiserRole()
    {
        foreach (var methodName in new[] { nameof(ResultsController.Create), nameof(ResultsController.Update) })
        {
            var authorize = typeof(ResultsController)
                .GetMethod(methodName)!
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .Single();
            Assert.Equal(nameof(UserRole.Organiser), authorize.Roles);
        }
    }

    [Fact]
    public async Task ParticipantCannotCreateAndUnauthenticatedCannotCreate()
    {
        await using var participantFixture = TestFixture.Create(UserRole.Participant);
        var participantEntry = participantFixture.AddEntry(participantFixture.User.UserID);
        var unauthenticatedFixture = TestFixture.Create(UserRole.Organiser, authenticated: false);
        var unauthenticatedEntry = unauthenticatedFixture.AddEntry(unauthenticatedFixture.User.UserID);

        var participantMetadata = typeof(ResultsController).GetMethod(nameof(ResultsController.Create))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>().Single();
        var unauthenticatedResult = await unauthenticatedFixture.Controller.Create(
            unauthenticatedEntry.EntryID, unauthenticatedFixture.CreateRequest(unauthenticatedEntry.EntryID), CancellationToken.None);

        Assert.Equal(nameof(UserRole.Organiser), participantMetadata.Roles);
        Assert.IsType<UnauthorizedResult>(unauthenticatedResult.Result);
        Assert.Empty(await participantFixture.DbContext.Results.ToListAsync());
    }

    [Fact]
    public async Task MissingEntryReturnsNotFound()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Create(999, fixture.CreateRequest(999), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task OrganiserCanUpdateOwnResultAndCannotChangeEntryRelationship()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var entry = fixture.AddEntry(fixture.User.UserID);
        var stored = fixture.AddResult(entry.EntryID);
        var originalRecordedAt = stored.RecordedAt;

        var result = await fixture.Controller.Update(stored.ResultID, new UpdateResultRequest
        {
            FinishTime = TimeSpan.FromMinutes(55),
            OverallPosition = 2,
            CategoryPosition = 1,
            ResultStatus = ResultStatus.Finished
        }, CancellationToken.None);

        var updated = await fixture.DbContext.Results.SingleAsync();
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(entry.EntryID, updated.EntryID);
        Assert.Equal(originalRecordedAt, updated.RecordedAt);
        Assert.Equal(2, updated.OverallPosition);
    }

    [Fact]
    public async Task ParticipantCanViewOnlyTheirOwnResults()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var ownEntry = fixture.AddEntry(fixture.User.UserID);
        var otherEntry = fixture.AddEntry(fixture.OtherParticipant.UserID);
        fixture.AddResult(ownEntry.EntryID);
        fixture.AddResult(otherEntry.EntryID);

        var result = await fixture.Controller.GetMine(CancellationToken.None);

        var results = Assert.IsType<List<ResultResponse>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(results);
        Assert.Equal(ownEntry.EntryID, results[0].EntryID);
    }

    [Fact]
    public async Task OrganiserCanViewOwnEventResultsAndNotAnotherEventsResults()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var ownEntry = fixture.AddEntry(fixture.OtherParticipant.UserID);
        var ownResult = fixture.AddResult(ownEntry.EntryID);
        var otherOrganizer = fixture.AddUser(UserRole.Organiser, "OTHER2@EXAMPLE.COM");
        var otherEvent = fixture.OtherEvent(otherOrganizer.UserID);
        var otherEntry = fixture.AddEntry(otherOrganizer.UserID, otherEvent);
        fixture.AddResult(otherEntry.EntryID);

        var own = await fixture.Controller.GetForEvent(fixture.Event.EventID, CancellationToken.None);
        var other = await fixture.Controller.GetForEvent(otherEvent.EventID, CancellationToken.None);

        var results = Assert.IsType<List<ResultResponse>>(Assert.IsType<OkObjectResult>(own.Result).Value);
        Assert.Single(results);
        Assert.Equal(ownResult.ResultID, results[0].ResultID);
        Assert.IsType<ForbidResult>(other.Result);
    }

    [Fact]
    public async Task MissingEventAndResultReturnNotFound()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var eventResult = await fixture.Controller.GetForEvent(999, CancellationToken.None);
        var result = await fixture.Controller.GetByID(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(eventResult.Result);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void ResultResponseDoesNotExposeNavigationOrPasswordData()
    {
        var properties = typeof(ResultResponse).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain("Entry", properties);
        Assert.DoesNotContain("User", properties);
        Assert.DoesNotContain("PasswordHash", properties);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task InvalidOverallPositionIsRejected(int position)
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var entry = fixture.AddEntry(fixture.User.UserID);
        var request = fixture.CreateRequest(entry.EntryID);
        request = new CreateResultRequest { EntryID = request.EntryID, FinishTime = request.FinishTime, OverallPosition = position, CategoryPosition = 1, ResultStatus = ResultStatus.Finished };

        var result = await fixture.Controller.Create(entry.EntryID, request, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(RaceDayDbContext dbContext, ResultsController controller, User user, User otherParticipant, Event @event)
        {
            DbContext = dbContext; Controller = controller; User = user; OtherParticipant = otherParticipant; Event = @event;
        }

        public RaceDayDbContext DbContext { get; }
        public ResultsController Controller { get; }
        public User User { get; }
        public User OtherParticipant { get; }
        public Event Event { get; }

        public static TestFixture Create(UserRole role, bool authenticated = true)
        {
            var options = new DbContextOptionsBuilder<RaceDayDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var dbContext = new RaceDayDbContext(options);
            var user = new User { FirstName = "Test", LastName = "User", Email = "USER@EXAMPLE.COM", PasswordHash = "hash", Role = role, CreatedAt = DateTime.UtcNow };
            var other = new User { FirstName = "Other", LastName = "Participant", Email = "OTHER@EXAMPLE.COM", PasswordHash = "hash", Role = UserRole.Participant, CreatedAt = DateTime.UtcNow };
            dbContext.Users.AddRange(user, other); dbContext.SaveChanges();
            var @event = AddEvent(dbContext, user.UserID, "Own Event");
            var controller = new ResultsController(dbContext);
            if (authenticated)
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("UserID", user.UserID.ToString()), new Claim("Role", role.ToString()) }, "TestAuth"))
                    }
                };
            }
            return new TestFixture(dbContext, controller, user, other, @event);
        }

        public User AddUser(UserRole role, string email)
        {
            var user = new User { FirstName = "Other", LastName = "Organizer", Email = email, PasswordHash = "hash", Role = role, CreatedAt = DateTime.UtcNow };
            DbContext.Users.Add(user); DbContext.SaveChanges(); return user;
        }

        public Event OtherEvent(int organizerID) => AddEvent(DbContext, organizerID, "Other Event");

        public Entry AddEntry(int userID, Event? eventEntity = null)
        {
            eventEntity ??= Event;
            var category = new Category { EventID = eventEntity.EventID, CategoryName = "10K", DistanceKM = 10, MaxParticipants = 100, EntryFee = 25 };
            DbContext.Categories.Add(category); DbContext.SaveChanges();
            var entry = new Entry { UserID = userID, CategoryID = category.CategoryID, EntryDate = DateTime.UtcNow };
            DbContext.Entries.Add(entry); DbContext.SaveChanges(); return entry;
        }

        public Result AddResult(int entryID)
        {
            var result = new Result { EntryID = entryID, FinishTime = TimeSpan.FromMinutes(60), OverallPosition = 1, CategoryPosition = 1, ResultStatus = ResultStatus.Finished, RecordedAt = DateTime.UtcNow };
            DbContext.Results.Add(result); DbContext.SaveChanges(); return result;
        }

        public CreateResultRequest CreateRequest(int entryID) => new() { EntryID = entryID, FinishTime = TimeSpan.FromMinutes(60), OverallPosition = 1, CategoryPosition = 1, ResultStatus = ResultStatus.Finished };

        private static Event AddEvent(RaceDayDbContext dbContext, int organizerID, string name)
        {
            var @event = new Event { OrganizerID = organizerID, EventName = name, Description = "Test", EventDate = new DateOnly(2026, 12, 31), Location = "Park", DistanceKM = 10, EventType = EventTypeOption.Run, RegistrationDeadline = new DateOnly(2026, 12, 1), CreatedAt = DateTime.UtcNow };
            dbContext.Events.Add(@event); dbContext.SaveChanges(); return @event;
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
