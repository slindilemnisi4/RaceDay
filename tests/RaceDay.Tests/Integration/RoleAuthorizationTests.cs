using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;                  // <-- for PostAsJsonAsync
using RaceDay.Tests.Infrastructure;
using Xunit;                                 // <-- for Assert, Fact, IClassFixture

namespace RaceDay.Tests.Integration;

public class RoleAuthorizationTests : IClassFixture<RaceDayWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RoleAuthorizationTests(RaceDayWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ParticipantCannotCreateEvent()
    {
        // Create a valid JWT for a Participant.
        var token = TestTokenHelper.CreateToken(
            userId: 1,
            email: "participant@test.com",
            role: "Participant");

        // Send the token as a Bearer authentication header.
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Attempt to create an event.
        var request = new
        {
            eventName = "Participant Test Event",
            description = "This event should not be created.",
            eventDate = DateTime.UtcNow.AddDays(30),
            location = "Pretoria",
            distanceKM = 10,
            eventType = "Run",
            registrationDeadline = DateTime.UtcNow.AddDays(20)
        };

        var response = await _client.PostAsJsonAsync("/api/events", request);

        // The Participant is authenticated but does not have the Organiser role.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}