using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RaceDay.Web.Services;

namespace RaceDay.Web.Filters;

public sealed class ApiAuthenticationRedirectFilter(
    ITempDataDictionaryFactory tempDataDictionaryFactory) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var isBrowserNavigation =
            HttpMethods.IsGet(httpContext.Request.Method)
            || HttpMethods.IsHead(httpContext.Request.Method);
        var requestsHtml = httpContext.Request.GetTypedHeaders()
            .Accept?.Any(mediaType =>
                string.Equals(
                    mediaType.MediaType.Value,
                    "text/html",
                    StringComparison.OrdinalIgnoreCase)) == true;
        var isAccountRoute = string.Equals(
            context.RouteData.Values["controller"]?.ToString(),
            "Account",
            StringComparison.OrdinalIgnoreCase);

        if (httpContext.Items.ContainsKey(ApiAuthenticationSession.AuthenticationRejectedItemKey)
            && requestsHtml
            && !isAccountRoute)
        {
            tempDataDictionaryFactory.GetTempData(httpContext)["AccountMessage"] =
                "Your sign-in session has expired or was rejected by the API. Please sign in again.";

            if (isBrowserNavigation)
            {
                var returnUrl = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";
                context.Result = new RedirectToActionResult(
                    "Login",
                    "Account",
                    new { returnUrl, sessionExpired = true });
            }
        }

        await next();
    }
}
