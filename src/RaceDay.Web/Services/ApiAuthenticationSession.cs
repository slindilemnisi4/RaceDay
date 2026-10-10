using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace RaceDay.Web.Services;

public sealed class ApiAuthenticationSession(IHttpContextAccessor httpContextAccessor)
{
    public const string AccessTokenSessionKey = "RaceDay.Api.AccessToken";
    public const string AuthenticationRejectedItemKey = "RaceDay.Api.AuthenticationRejected";

    public string? GetAccessToken()
    {
        return httpContextAccessor.HttpContext?.Session.GetString(AccessTokenSessionKey);
    }

    public void StoreAccessToken(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        GetSession().SetString(AccessTokenSessionKey, accessToken);
    }

    public void MarkAuthenticationRejected()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            httpContext.Items[AuthenticationRejectedItemKey] = true;
        }
    }

    public async Task ClearAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        httpContext.Session.Remove(AccessTokenSessionKey);
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private ISession GetSession()
    {
        return httpContextAccessor.HttpContext?.Session
            ?? throw new InvalidOperationException("Session is unavailable for this request.");
    }
}
