using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using RaceDay.Tests.Infrastructure;
using Xunit;

namespace RaceDay.Tests.Integration;

public sealed class EventMediaRequestValidationTests
{
    [Fact]
    public async Task OrganiserCanCreateEventWithImageThroughApiPipeline()
    {
        using var factory = new RaceDayWebApplicationFactory();
        using var client = await CreateOrganiserClientAsync(factory);
        var image = CreateImageRequest();

        var response = await client.PostAsJsonAsync("/api/events", CreateEventRequest(image));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var eventResponse = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.NotNull(eventResponse);
        Assert.Equal(image.RelativePath, Assert.Single(eventResponse.Media).RelativePath);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var persistedMedia = await dbContext.EventMedia.SingleAsync();
        Assert.Equal(eventResponse.EventID, persistedMedia.EventID);
        Assert.Equal(image.RelativePath, persistedMedia.RelativePath);
    }

    [Fact]
    public async Task OrganiserCanEditEventWithImageThroughApiPipeline()
    {
        using var factory = new RaceDayWebApplicationFactory();
        using var client = await CreateOrganiserClientAsync(factory);
        var createResponse = await client.PostAsJsonAsync("/api/events", CreateEventRequest());
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdEvent = await createResponse.Content.ReadFromJsonAsync<EventResponse>();
        Assert.NotNull(createdEvent);
        var image = CreateImageRequest();

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/events/{createdEvent.EventID}",
            CreateEventRequest(image));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedEvent = await updateResponse.Content.ReadFromJsonAsync<EventResponse>();
        Assert.NotNull(updatedEvent);
        Assert.Equal(image.RelativePath, Assert.Single(updatedEvent.Media).RelativePath);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var persistedMedia = await dbContext.EventMedia.SingleAsync();
        Assert.Equal(createdEvent.EventID, persistedMedia.EventID);
        Assert.Equal(image.RelativePath, persistedMedia.RelativePath);
    }

    [Fact]
    public async Task MissingRequiredMediaMetadataReturnsBadRequest()
    {
        using var factory = new RaceDayWebApplicationFactory();
        using var client = await CreateOrganiserClientAsync(factory);
        var image = CreateImageRequest();
        var request = new
        {
            eventName = "Media validation test",
            description = "A test event.",
            eventDate = new DateOnly(2026, 12, 10),
            location = "Test venue",
            distanceKM = 5,
            entryFee = 0,
            eventType = 0,
            registrationDeadline = new DateOnly(2026, 12, 1),
            media = new[]
            {
                new
                {
                    mediaType = (int)image.MediaType,
                    relativePath = image.RelativePath,
                    contentType = (string?)null,
                    sizeBytes = image.SizeBytes,
                    durationSeconds = image.DurationSeconds,
                    displayOrder = image.DisplayOrder
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/events", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpClient> CreateOrganiserClientAsync(
        RaceDayWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var organiser = new User
        {
            FirstName = "Test",
            LastName = "Organiser",
            Email = $"{Guid.NewGuid():N}@example.test",
            PasswordHash = "test-hash",
            Role = UserRole.Organiser,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(organiser);
        await dbContext.SaveChangesAsync();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokenHelper.CreateToken(organiser.UserID, organiser.Email, "Organiser"));
        return client;
    }

    private static object CreateEventRequest(params EventMediaRequest[] media) => new
    {
        eventName = "Media validation test",
        description = "A test event.",
        eventDate = new DateOnly(2026, 12, 10),
        location = "Test venue",
        distanceKM = 5,
        entryFee = 0,
        eventType = 0,
        registrationDeadline = new DateOnly(2026, 12, 1),
        media
    };

    private static EventMediaRequest CreateImageRequest() => new(
        EventMediaType.Image,
        $"/EventMedia/{Guid.NewGuid():N}.png",
        "image/png",
        128,
        null,
        0);
}
