using System.Net;
using System.Net.Http.Json;

namespace RaceDay.Web.Services;

public sealed class RaceDayApiClient(HttpClient httpClient)
{
    public Task<TResponse?> GetAsync<TResponse>(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        return httpClient.GetFromJsonAsync<TResponse>(requestUri, cancellationToken);
    }

    public async Task<RaceDayApiResponse> PostAsync<TRequest>(
        string requestUri,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(request)
        };
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var errorContent = response.IsSuccessStatusCode
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        return new RaceDayApiResponse(response.StatusCode, errorContent);
    }
}

public sealed record RaceDayApiResponse(HttpStatusCode StatusCode, string? ErrorContent);
