using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RaceDay.Web.Models.Account;
using RaceDay.Web.Services;

namespace RaceDay.Web.Controllers;

public sealed class AccountController(
    RaceDayApiClient apiClient,
    ApiJwtTokenValidator tokenValidator,
    ApiAuthenticationSession authenticationSession) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        RaceDayApiResponse apiResponse;
        try
        {
            apiResponse = await apiClient.PostAsync(
                "api/auth/register",
                new RegisterApiRequest(
                    model.FirstName,
                    model.LastName,
                    model.Email,
                    model.Password,
                    Role: 1),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ShowRegistrationError(
                model,
                "Registration is temporarily unavailable. Please try again later.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ShowRegistrationError(
                model,
                "The registration request timed out. Please try again.");
        }

        if (apiResponse.StatusCode == HttpStatusCode.Created)
        {
            TempData["AccountMessage"] =
                "Your Participant account has been created. Please sign in; registration did not sign you in automatically.";
            return RedirectToAction(nameof(Login));
        }

        if (apiResponse.StatusCode == HttpStatusCode.Conflict)
        {
            return ShowRegistrationError(
                model,
                "An account with that email address already exists.");
        }

        if (apiResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            AddApiValidationErrors(apiResponse.ErrorContent);
            return ShowRegistrationError(
                model,
                "Please check the registration details and try again.");
        }

        return ShowRegistrationError(
            model,
            "Registration could not be completed. Please try again later.");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null, bool sessionExpired = false)
    {
        ViewData["AccountMessage"] = sessionExpired
            ? TempData["AccountMessage"]
                ?? "Your sign-in session has expired. Please sign in again."
            : TempData["AccountMessage"];
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        RaceDayApiResponse<LoginApiResponse> apiResponse;
        try
        {
            apiResponse = await apiClient.PostForResponseAsync<LoginApiRequest, LoginApiResponse>(
                "api/auth/login",
                new LoginApiRequest(model.Email, model.Password),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ShowLoginError(
                model,
                "Sign in is temporarily unavailable. Please try again later.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ShowLoginError(
                model,
                "The sign-in request timed out. Please try again.");
        }
        catch (JsonException)
        {
            return ShowLoginError(
                model,
                "The API returned an invalid sign-in response. Please try again.");
        }

        if (apiResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            return ShowLoginError(model, "Invalid email or password.");
        }

        if (apiResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            AddApiValidationErrors(apiResponse.ErrorContent);
            return ShowLoginError(
                model,
                "Please check the sign-in details and try again.");
        }

        if (apiResponse.StatusCode != HttpStatusCode.OK || apiResponse.Data is null)
        {
            return ShowLoginError(
                model,
                "Sign in could not be completed. Please try again later.");
        }

        ValidatedApiIdentity identity;
        try
        {
            identity = tokenValidator.Validate(apiResponse.Data);
        }
        catch (SecurityTokenException)
        {
            return ShowLoginError(
                model,
                "The API returned an invalid sign-in response. Please try again.");
        }
        catch (ArgumentException)
        {
            return ShowLoginError(
                model,
                "The API returned an invalid sign-in response. Please try again.");
        }
        catch (InvalidOperationException)
        {
            return ShowLoginError(
                model,
                "Sign in is not configured correctly. Please contact the site administrator.");
        }

        if (identity.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return ShowLoginError(
                model,
                "Your sign-in response has expired. Please try again.");
        }

        authenticationSession.StoreAccessToken(apiResponse.Data.Token);

        try
        {
            var profileResponse = await apiClient.GetAsync<UserProfileApiResponse>(
                "api/users/me",
                cancellationToken);

            if (profileResponse.StatusCode != HttpStatusCode.OK
                || profileResponse.Data is null)
            {
                await authenticationSession.ClearAsync();
                return ShowLoginError(
                    model,
                    "Your account could not be verified. Please try again.");
            }

            identity = tokenValidator.ValidateProfile(identity, profileResponse.Data);
        }
        catch (HttpRequestException)
        {
            await authenticationSession.ClearAsync();
            return ShowLoginError(
                model,
                "Sign in is temporarily unavailable. Please try again later.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await authenticationSession.ClearAsync();
            return ShowLoginError(
                model,
                "The sign-in request timed out. Please try again.");
        }
        catch (SecurityTokenException)
        {
            await authenticationSession.ClearAsync();
            return ShowLoginError(
                model,
                "The API returned an invalid account profile. Please try again.");
        }
        catch (JsonException)
        {
            await authenticationSession.ClearAsync();
            return ShowLoginError(
                model,
                "The API returned an invalid account profile. Please try again.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new Claim("UserID", identity.UserId.ToString()),
            new Claim(ClaimTypes.Email, identity.Email),
            new Claim(
                ClaimTypes.Name,
                string.Join(' ', identity.FirstName, identity.LastName)),
            new Claim(ClaimTypes.Role, identity.Role)
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = identity.ExpiresAt
        };

        var signInCompleted = false;
        try
        {
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                properties);
            signInCompleted = true;
        }
        finally
        {
            if (!signInCompleted)
            {
                await authenticationSession.ClearAsync();
            }
        }

        model.Password = string.Empty;
        ModelState.Remove(nameof(model.Password));

        if (Url.IsLocalUrl(model.ReturnUrl))
        {
            return LocalRedirect(model.ReturnUrl!);
        }

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await authenticationSession.ClearAsync();
        TempData["AccountMessage"] =
            "You are signed out of RaceDay. The API does not revoke issued tokens, so any copied token remains valid until it expires.";
        return RedirectToAction(nameof(Login));
    }

    private IActionResult ShowRegistrationError(RegisterViewModel model, string message)
    {
        ClearRegistrationPasswords(model);
        ViewData["ApiMessage"] = message;
        return View(nameof(Register), model);
    }

    private IActionResult ShowLoginError(LoginViewModel model, string message)
    {
        model.Password = string.Empty;
        ModelState.Remove(nameof(model.Password));
        ViewData["ApiMessage"] = message;
        return View(nameof(Login), model);
    }

    private void AddApiValidationErrors(string? errorContent)
    {
        if (string.IsNullOrWhiteSpace(errorContent))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(errorContent);
            if (!document.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var field in errors.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var error in field.Value.EnumerateArray())
                {
                    if (error.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(error.GetString()))
                    {
                        ModelState.AddModelError(field.Name, error.GetString()!);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // The generic account error remains visible if the API body is not
            // a standard validation-problem response.
        }
    }

    private void ClearRegistrationPasswords(RegisterViewModel model)
    {
        model.Password = string.Empty;
        model.ConfirmPassword = string.Empty;
        ModelState.Remove(nameof(model.Password));
        ModelState.Remove(nameof(model.ConfirmPassword));
    }

    private sealed record RegisterApiRequest(
        string FirstName,
        string LastName,
        string Email,
        string Password,
        int Role);

    private sealed record LoginApiRequest(string Email, string Password);
}
