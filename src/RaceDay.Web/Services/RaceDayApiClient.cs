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

    public async Task<TResponse?> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string requestUri,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(request)
        };
        using var response = await httpClient.SendAsync(message, cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(
            cancellationToken: cancellationToken);
    }
}
