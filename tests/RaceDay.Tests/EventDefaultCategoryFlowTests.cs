using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Controllers;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using Xunit;

namespace RaceDay.Tests;

public sealed class EventDefaultCategoryFlowTests
{
    [Fact]
    public async Task ParticipantCanRetrieveAndEnrolInEventDefaultCategory()
    {
        var options = new DbContextOptionsBuilder<RaceDayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new RaceDayDbContext(options);
        var organiser = CreateUser("ORGANISER@EXAMPLE.COM", UserRole.Organiser);
        var participant = CreateUser("PARTICIPANT@EXAMPLE.COM", UserRole.Participant);
        dbContext.Users.AddRange(organiser, participant);
        await dbContext.SaveChangesAsync();

        var eventsController = new EventsController(dbContext);
        SetIdentity(eventsController, organiser);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var createResult = await eventsController.Create(
            new CreateEventRequest
            {
                EventName = "Default category event",
                Description = "An event that should be ready to join.",
                EventDate = today.AddDays(30),
                Location = "RaceDay Park",
                DistanceKM = 10,
                EntryFee = 150m,
                EventType = EventTypeOption.Run,
                RegistrationDeadline = today.AddDays(20)
            },
            CancellationToken.None);

        var created = Assert.IsType<EventResponse>(
            Assert.IsType<CreatedResult>(createResult.Result).Value);
        var listResult = await eventsController.GetAll(CancellationToken.None);
        var listedEvent = Assert.Single(
            Assert.IsType<List<EventResponse>>(
                Assert.IsType<OkObjectResult>(listResult.Result).Value));
        var categoryController = new CategoriesController(dbContext);
        var categoryResult = await categoryController.GetAll(CancellationToken.None);
        var category = Assert.Single(
            Assert.IsType<List<CategoryResponse>>(
                Assert.IsType<OkObjectResult>(categoryResult.Result).Value));

        var enrolmentsController = new EnrolmentsController(dbContext);
        SetIdentity(enrolmentsController, participant);
        var enrolmentResult = await enrolmentsController.Create(
            created.EventID,
            new CreateEnrolmentRequest { CategoryID = category.CategoryID },
            CancellationToken.None);

        Assert.Equal("General Entry", category.CategoryName);
        Assert.Equal(150m, category.EntryFee);
        Assert.Equal(150m, created.EntryFee);
        Assert.Equal(150m, listedEvent.EntryFee);
        Assert.IsType<CreatedResult>(enrolmentResult.Result);
        Assert.Equal(category.CategoryID, (await dbContext.Entries.SingleAsync()).CategoryID);
    }

    private static User CreateUser(string email, UserRole role) =>
        new()
        {
            FirstName = "Test",
            LastName = role.ToString(),
            Email = email,
            PasswordHash = "hash",
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

    private static void SetIdentity(ControllerBase controller, User user)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[]
                    {
                        new Claim("UserID", user.UserID.ToString()),
                        new Claim("Role", user.Role.ToString())
                    },
                    authenticationType: "TestAuth"))
            }
        };
    }
}
