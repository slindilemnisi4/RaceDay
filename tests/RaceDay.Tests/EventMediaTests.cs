using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaceDay.API.Controllers;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using Xunit;

namespace RaceDay.Tests;

public sealed class EventMediaTests
{
    [Fact]
    public async Task CreateStoresAndReturnsValidatedMediaMetadata()
    {
        await using var fixture = TestFixture.Create();
        var image = ValidImage(displayOrder: 0);
        var video = ValidVideo(displayOrder: 1);

        var result = await fixture.Controller.Create(
            fixture.ValidCreateRequest(image, video),
            CancellationToken.None);

        var response = Assert.IsType<EventResponse>(Assert.IsType<CreatedResult>(result.Result).Value);
        Assert.Collection(
            response.Media,
            item =>
            {
                Assert.Equal(image.RelativePath, item.RelativePath);
                Assert.Equal(EventMediaType.Image, item.MediaType);
            },
            item =>
            {
                Assert.Equal(video.RelativePath, item.RelativePath);
                Assert.Equal(EventMediaType.Video, item.MediaType);
                Assert.Equal(12.5, item.DurationSeconds);
            });
        Assert.Equal(2, await fixture.DbContext.EventMedia.CountAsync());
    }

    [Fact]
    public async Task GetByIdReturnsStoredMediaInDisplayOrder()
    {
        await using var fixture = TestFixture.Create();
        var later = ValidImage(displayOrder: 2);
        var earlier = ValidImage(displayOrder: 0);
        var eventEntity = fixture.AddEvent(earlier, later);

        var result = await fixture.Controller.GetByID(eventEntity.EventID, CancellationToken.None);

        var response = Assert.IsType<EventResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(
            new[] { earlier.RelativePath, later.RelativePath },
            response.Media.Select(item => item.RelativePath));
    }

    [Fact]
    public async Task UpdateReplacesMediaAndNullMediaLeavesExistingMediaUnchanged()
    {
        await using var fixture = TestFixture.Create();
        var originalMedia = ValidImage(displayOrder: 0);
        var eventEntity = fixture.AddEvent(originalMedia);

        var omittedMediaResult = await fixture.Controller.Update(
            eventEntity.EventID,
            fixture.ValidUpdateRequest(media: null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(omittedMediaResult.Result);
        Assert.Equal(originalMedia.RelativePath, (await fixture.DbContext.EventMedia.SingleAsync()).RelativePath);

        var replacement = ValidImage(displayOrder: 0);
        var replacementResult = await fixture.Controller.Update(
            eventEntity.EventID,
            fixture.ValidUpdateRequest(replacement),
            CancellationToken.None);

        var response = Assert.IsType<EventResponse>(
            Assert.IsType<OkObjectResult>(replacementResult.Result).Value);
        Assert.Equal(replacement.RelativePath, Assert.Single(response.Media).RelativePath);
        Assert.Equal(replacement.RelativePath, (await fixture.DbContext.EventMedia.SingleAsync()).RelativePath);
    }

    [Fact]
    public async Task OrganiserCannotUpdateAnotherOrganisersEventMedia()
    {
        await using var fixture = TestFixture.Create();
        var otherOrganiser = fixture.AddOrganiser("OTHER@EXAMPLE.COM");
        var originalMedia = ValidImage(displayOrder: 0);
        var eventEntity = fixture.AddEventFor(otherOrganiser.UserID, originalMedia);

        var result = await fixture.Controller.Update(
            eventEntity.EventID,
            fixture.ValidUpdateRequest(ValidImage(displayOrder: 0)),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal(originalMedia.RelativePath, (await fixture.DbContext.EventMedia.SingleAsync()).RelativePath);
    }

    [Fact]
    public async Task UnauthenticatedRequestCannotCreateEventMedia()
    {
        await using var fixture = TestFixture.Create(authenticated: false);

        var result = await fixture.Controller.Create(
            fixture.ValidCreateRequest(ValidImage(displayOrder: 0)),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Empty(await fixture.DbContext.Events.ToListAsync());
        Assert.Empty(await fixture.DbContext.EventMedia.ToListAsync());
    }

    [Fact]
    public async Task UpdateForMissingEventReturnsNotFoundWithoutPersistingMedia()
    {
        await using var fixture = TestFixture.Create();

        var result = await fixture.Controller.Update(
            999,
            fixture.ValidUpdateRequest(ValidImage(displayOrder: 0)),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(await fixture.DbContext.EventMedia.ToListAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidMediaRequests))]
    public async Task CreateRejectsInvalidMediaMetadata(IReadOnlyList<EventMediaRequest> media)
    {
        await using var fixture = TestFixture.Create();

        var result = await fixture.Controller.Create(
            fixture.ValidCreateRequest(media.ToArray()),
            CancellationToken.None);

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        Assert.NotEmpty(Assert.IsType<ValidationProblemDetails>(badRequest.Value).Errors);
        Assert.Empty(await fixture.DbContext.Events.ToListAsync());
        Assert.Empty(await fixture.DbContext.EventMedia.ToListAsync());
    }

    public static IEnumerable<object[]> InvalidMediaRequests()
    {
        yield return [new[] { ValidImage(relativePath: "/EventMedia/not-a-guid.jpg") }];
        yield return [new[] { ValidImage(relativePath: "/EventMedia/../outside.jpg") }];
        yield return [new[] { ValidImage(contentType: "image/gif") }];
        yield return [new[] { ValidImage(sizeBytes: 5_242_881) }];
        yield return [new[] { ValidVideo(durationSeconds: 30.1) }];
        yield return [new[] { ValidImage(displayOrder: -1) }];
        yield return [Enumerable.Range(0, 6).Select(index => ValidImage(displayOrder: index)).ToArray()];
        yield return [new[] { ValidVideo(), ValidVideo(displayOrder: 1) }];

        var duplicatePath = "/EventMedia/0123456789abcdef0123456789abcdef.jpg";
        yield return
        [
            new[]
            {
                ValidImage(relativePath: duplicatePath, displayOrder: 0),
                ValidImage(relativePath: duplicatePath, displayOrder: 1)
            }
        ];
        yield return
        [
            new[]
            {
                new EventMediaRequest(
                    (EventMediaType)99,
                    $"/EventMedia/{Guid.NewGuid():N}.jpg",
                    "image/jpeg",
                    1024,
                    null,
                    0)
            }
        ];
    }

    [Fact]
    public async Task DeleteRemovesEventMediaMetadataWithEvent()
    {
        await using var fixture = TestFixture.Create();
        var eventEntity = fixture.AddEvent(ValidImage(displayOrder: 0));

        var result = await fixture.Controller.Delete(eventEntity.EventID, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await fixture.DbContext.Events.ToListAsync());
        Assert.Empty(await fixture.DbContext.EventMedia.ToListAsync());
    }

    private static EventMediaRequest ValidImage(
        int displayOrder = 0,
        string? relativePath = null,
        string contentType = "image/jpeg",
        long sizeBytes = 1024) =>
        new(
            EventMediaType.Image,
            relativePath ?? $"/EventMedia/{Guid.NewGuid():N}.jpg",
            contentType,
            sizeBytes,
            null,
            displayOrder);

    private static EventMediaRequest ValidVideo(
        int displayOrder = 0,
        double durationSeconds = 12.5) =>
        new(
            EventMediaType.Video,
            $"/EventMedia/{Guid.NewGuid():N}.mp4",
            "video/mp4",
            2048,
            durationSeconds,
            displayOrder);

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

        public static TestFixture Create(bool authenticated = true)
        {
            var options = new DbContextOptionsBuilder<RaceDayDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new RaceDayDbContext(options);
            var user = new User
            {
                FirstName = "Test",
                LastName = "Organiser",
                Email = "ORGANISER@EXAMPLE.COM",
                PasswordHash = "hashed-password",
                Role = UserRole.Organiser,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
            dbContext.SaveChanges();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EventMedia:PublicPath"] = "/EventMedia"
                })
                .Build();
            var controller = new EventsController(dbContext, configuration);
            if (authenticated)
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [
                                new Claim("UserID", user.UserID.ToString()),
                                new Claim("Role", UserRole.Organiser.ToString())
                            ],
                            authenticationType: "TestAuth"))
                    }
                };
            }

            return new TestFixture(dbContext, controller, user);
        }

        public User AddOrganiser(string email)
        {
            var user = new User
            {
                FirstName = "Other",
                LastName = "Organiser",
                Email = email,
                PasswordHash = "hashed-password",
                Role = UserRole.Organiser,
                CreatedAt = DateTime.UtcNow
            };
            DbContext.Users.Add(user);
            DbContext.SaveChanges();
            return user;
        }

        public Event AddEvent(params EventMediaRequest[] media) =>
            AddEventFor(User.UserID, media);

        public Event AddEventFor(int organizerId, params EventMediaRequest[] media)
        {
            var eventEntity = new Event
            {
                OrganizerID = organizerId,
                EventName = "Test Race",
                Description = "A test event.",
                EventDate = new DateOnly(2026, 5, 10),
                Location = "RaceDay Park",
                DistanceKM = 10,
                EventType = EventTypeOption.Run,
                RegistrationDeadline = new DateOnly(2026, 5, 1),
                CreatedAt = DateTime.UtcNow,
                Categories =
                [
                    new Category { CategoryName = "General Entry", EntryFee = 0 }
                ],
                Media = media.Select(item => new EventMedia
                {
                    MediaType = item.MediaType,
                    RelativePath = item.RelativePath,
                    ContentType = item.ContentType,
                    SizeBytes = item.SizeBytes,
                    DurationSeconds = item.DurationSeconds,
                    DisplayOrder = item.DisplayOrder
                }).ToList()
            };
            DbContext.Events.Add(eventEntity);
            DbContext.SaveChanges();
            return eventEntity;
        }

        public CreateEventRequest ValidCreateRequest(params EventMediaRequest[] media) => new()
        {
            EventName = "Spring Race",
            Description = "A local running event.",
            EventDate = new DateOnly(2026, 5, 10),
            Location = "RaceDay Park",
            DistanceKM = 10,
            EventType = EventTypeOption.Run,
            RegistrationDeadline = new DateOnly(2026, 5, 1),
            Media = media
        };

        public UpdateEventRequest ValidUpdateRequest(params EventMediaRequest[]? media) => new()
        {
            EventName = "Updated Test Race",
            Description = "An updated test event.",
            EventDate = new DateOnly(2026, 5, 10),
            Location = "RaceDay Park",
            DistanceKM = 10,
            EventType = EventTypeOption.Run,
            RegistrationDeadline = new DateOnly(2026, 5, 1),
            Media = media
        };

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
