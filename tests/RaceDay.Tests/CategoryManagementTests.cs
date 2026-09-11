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

public sealed class CategoryManagementTests
{
    [Fact]
    public async Task OrganiserCanCreateCategoryForOwnEvent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent(fixture.User.UserID);

        var result = await fixture.Controller.Create(
            fixture.CreateRequest(eventEntity.EventID),
            CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var response = Assert.IsType<CategoryResponse>(created.Value);
        var stored = await fixture.DbContext.Categories.SingleAsync();
        Assert.Equal(eventEntity.EventID, stored.EventID);
        Assert.Equal("10K", response.CategoryName);
    }

    [Fact]
    public void ParticipantCannotCreateCategoryThroughAuthorizationMetadata()
    {
        var authorize = typeof(CategoriesController)
            .GetMethod(nameof(CategoriesController.Create))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(nameof(UserRole.Organiser), authorize.Roles);
    }

    [Fact]
    public async Task OrganiserCannotCreateCategoryForAnotherOrganisersEvent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var otherUser = fixture.AddUser(UserRole.Organiser, "OTHER@EXAMPLE.COM");
        var eventEntity = fixture.AddEvent(otherUser.UserID);

        var result = await fixture.Controller.Create(
            fixture.CreateRequest(eventEntity.EventID),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task MissingEventReturnsNotFoundOnCreate()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.Create(
            fixture.CreateRequest(999),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task OrganiserAndParticipantCanViewCategories()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var eventEntity = fixture.AddEvent(fixture.User.UserID);
        fixture.AddCategory(eventEntity.EventID);

        var result = await fixture.Controller.GetAll(CancellationToken.None);

        var categories = Assert.IsType<List<CategoryResponse>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(categories);
    }

    [Fact]
    public async Task GetByIDReturnsCategoryAndMissingIDReturnsNotFound()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent(fixture.User.UserID);
        var category = fixture.AddCategory(eventEntity.EventID);

        var found = await fixture.Controller.GetByID(category.CategoryID, CancellationToken.None);
        var missing = await fixture.Controller.GetByID(999, CancellationToken.None);

        Assert.Equal(category.CategoryID, Assert.IsType<CategoryResponse>(Assert.IsType<OkObjectResult>(found.Result).Value).CategoryID);
        Assert.IsType<NotFoundResult>(missing.Result);
    }

    [Fact]
    public async Task OrganiserCanUpdateOwnCategoryWithoutChangingEvent()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent(fixture.User.UserID);
        var category = fixture.AddCategory(eventEntity.EventID);

        var result = await fixture.Controller.Update(
            category.CategoryID,
            fixture.UpdateRequest(),
            CancellationToken.None);

        var updated = await fixture.DbContext.Categories.SingleAsync();
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(category.CategoryID, updated.CategoryID);
        Assert.Equal(eventEntity.EventID, updated.EventID);
        Assert.Equal("Updated", updated.CategoryName);
    }

    [Fact]
    public async Task OrganiserCannotUpdateOrDeleteAnotherOrganisersCategory()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var otherUser = fixture.AddUser(UserRole.Organiser, "OTHER@EXAMPLE.COM");
        var eventEntity = fixture.AddEvent(otherUser.UserID);
        var category = fixture.AddCategory(eventEntity.EventID);

        var update = await fixture.Controller.Update(category.CategoryID, fixture.UpdateRequest(), CancellationToken.None);
        var delete = await fixture.Controller.Delete(category.CategoryID, CancellationToken.None);

        Assert.IsType<ForbidResult>(update.Result);
        Assert.IsType<ForbidResult>(delete);
        Assert.NotNull(await fixture.DbContext.Categories.FindAsync(category.CategoryID));
    }

    [Fact]
    public async Task OrganiserCanDeleteOwnCategory()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent(fixture.User.UserID);
        var category = fixture.AddCategory(eventEntity.EventID);

        var result = await fixture.Controller.Delete(category.CategoryID, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await fixture.DbContext.Categories.FindAsync(category.CategoryID));
    }

    [Fact]
    public async Task MissingCategoryReturnsNotFoundOnUpdateAndDelete()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var update = await fixture.Controller.Update(999, fixture.UpdateRequest(), CancellationToken.None);
        var delete = await fixture.Controller.Delete(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(update.Result);
        Assert.IsType<NotFoundResult>(delete);
    }

    [Fact]
    public async Task InvalidCategoryDataIsRejected()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent(fixture.User.UserID);
        var invalid = new CreateCategoryRequest
        {
            EventID = eventEntity.EventID,
            CategoryName = " ",
            DistanceKM = 0,
            MaxParticipants = 0,
            EntryFee = -1
        };

        var result = await fixture.Controller.Create(invalid, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }

    [Theory]
    [MemberData(nameof(InvalidUpdateRequests))]
    public async Task InvalidCategoryUpdateDataIsRejected(UpdateCategoryRequest request)
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);
        var eventEntity = fixture.AddEvent(fixture.User.UserID);
        var category = fixture.AddCategory(eventEntity.EventID);

        var result = await fixture.Controller.Update(
            category.CategoryID,
            request,
            CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
    }

    public static IEnumerable<object[]> InvalidUpdateRequests() =>
        new[]
        {
            new object[] { new UpdateCategoryRequest { CategoryName = " ", DistanceKM = 10, MaxParticipants = 10, EntryFee = 1 } },
            new object[] { new UpdateCategoryRequest { CategoryName = "Valid", DistanceKM = 0, MaxParticipants = 10, EntryFee = 1 } },
            new object[] { new UpdateCategoryRequest { CategoryName = "Valid", DistanceKM = 10, MaxParticipants = 10, EntryFee = -1 } }
        };

    [Fact]
    public void CategoryAuthorizationMetadataMatchesRequirements()
    {
        var controllerAuthorization = typeof(CategoriesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();
        Assert.Null(controllerAuthorization.Roles);

        foreach (var methodName in new[] { nameof(CategoriesController.Create), nameof(CategoriesController.Update), nameof(CategoriesController.Delete) })
        {
            var authorization = typeof(CategoriesController)
                .GetMethod(methodName)!
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .Single();
            Assert.Equal(nameof(UserRole.Organiser), authorization.Roles);
        }
    }

    [Fact]
    public void CategoryEntryRelationshipRemainsRestricted()
    {
        using var fixture = TestFixture.Create(UserRole.Organiser);
        var foreignKey = fixture.DbContext.Model.FindEntityType(typeof(Entry))!
            .GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(Category));

        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void CategoryResponseDoesNotExposeNavigationOrSensitiveFields()
    {
        var properties = typeof(CategoryResponse).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain("Event", properties);
        Assert.DoesNotContain("Entries", properties);
        Assert.DoesNotContain("PasswordHash", properties);
    }

    private sealed class TestFixture : IDisposable, IAsyncDisposable
    {
        private TestFixture(RaceDayDbContext dbContext, CategoriesController controller, User user)
        {
            DbContext = dbContext;
            Controller = controller;
            User = user;
        }

        public RaceDayDbContext DbContext { get; }
        public CategoriesController Controller { get; }
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
            var controller = new CategoriesController(dbContext);
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
            return new TestFixture(dbContext, controller, user);
        }

        public User AddUser(UserRole role, string email)
        {
            var user = new User
            {
                FirstName = "Other",
                LastName = "User",
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

        public CreateCategoryRequest CreateRequest(int eventID) => new()
        {
            EventID = eventID,
            CategoryName = "10K",
            DistanceKM = 10,
            MaxParticipants = 100,
            EntryFee = 25
        };

        public UpdateCategoryRequest UpdateRequest() => new()
        {
            CategoryName = "Updated",
            DistanceKM = 5,
            MaxParticipants = 50,
            EntryFee = 15
        };

        public void Dispose() => DbContext.Dispose();

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
