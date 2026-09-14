using System.Net;
using RaceDay.Tests.Infrastructure;
using Xunit;                                  // <-- added this

namespace RaceDay.Tests.Integration;

public class AuthenticationPipelineTests : IClassFixture<RaceDayWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthenticationPipelineTests(RaceDayWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UnauthenticatedUserCannotAccessProtectedEndpoint()
    {
        // No authentication token is supplied with this request.
        var response = await _client.GetAsync("/api/results/me");

        // The authentication middleware should reject the request.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}