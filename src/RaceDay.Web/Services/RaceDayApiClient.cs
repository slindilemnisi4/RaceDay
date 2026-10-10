using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RaceDay.Web.Services;

public sealed class RaceDayApiClient(
    HttpClient httpClient,
    ApiAuthenticationSession authenticationSession)
{
    public Task<RaceDayApiResponse<TResponse>> GetAsync<TResponse>(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<object, TResponse>(
            HttpMethod.Get,
            requestUri,
            request: null,
            authenticationSession.GetAccessToken(),
            requiresAuthentication: true,
            cancellationToken);
    }

    public Task<RaceDayApiResponse<TResponse>> SendAuthenticatedAsync<TRequest, TResponse>(
        HttpMethod method,
        string requestUri,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TRequest, TResponse>(
            method,
            requestUri,
            request,
            authenticationSession.GetAccessToken(),
            requiresAuthentication: true,
            cancellationToken);
    }

    public async Task<RaceDayApiResponse> PostAsync<TRequest>(
        string requestUri,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<TRequest, object>(
            HttpMethod.Post,
            requestUri,
            request,
            accessToken: null,
            requiresAuthentication: false,
            cancellationToken);

        return new RaceDayApiResponse(response.StatusCode, response.ErrorContent);
    }

    public Task<RaceDayApiResponse<TResponse>> PostForResponseAsync<TRequest, TResponse>(
        string requestUri,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TRequest, TResponse>(
            HttpMethod.Post,
            requestUri,
            request,
            accessToken: null,
            requiresAuthentication: false,
            cancellationToken);
    }

    private async Task<RaceDayApiResponse<TResponse>> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string requestUri,
        TRequest? request,
        string? accessToken,
        bool requiresAuthentication,
        CancellationToken cancellationToken)
    {
        if (requiresAuthentication && string.IsNullOrWhiteSpace(accessToken))
        {
            await authenticationSession.ClearAsync();
            authenticationSession.MarkAuthenticationRejected();
            return new RaceDayApiResponse<TResponse>(HttpStatusCode.Unauthorized, default, null);
        }

        using var message = new HttpRequestMessage(method, requestUri);
        if (request is not null)
        {
            message.Content = JsonContent.Create(request);
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (requiresAuthentication && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await authenticationSession.ClearAsync();
            authenticationSession.MarkAuthenticationRejected();
        }

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new RaceDayApiResponse<TResponse>(
                response.StatusCode,
                default,
                responseContent);
        }

        var data = string.IsNullOrWhiteSpace(responseContent)
            ? default
            : JsonSerializer.Deserialize<TResponse>(
                responseContent,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return new RaceDayApiResponse<TResponse>(response.StatusCode, data, null);
    }
}

public sealed record RaceDayApiResponse(HttpStatusCode StatusCode, string? ErrorContent);

public sealed record RaceDayApiResponse<TResponse>(
    HttpStatusCode StatusCode,
    TResponse? Data,
    string? ErrorContent);
