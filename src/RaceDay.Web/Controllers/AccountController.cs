using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaceDay.Web.Models.Account;
using RaceDay.Web.Services;

namespace RaceDay.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController(RaceDayApiClient apiClient) : Controller
{
    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

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

    [HttpGet]
    public IActionResult Login()
    {
        ViewData["AccountMessage"] = TempData["AccountMessage"];
        return View(new LoginViewModel());
    }

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

        RaceDayApiResponse apiResponse;
        try
        {
            apiResponse = await apiClient.PostAsync(
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

        if (apiResponse.StatusCode == HttpStatusCode.OK)
        {
            model.Password = string.Empty;
            ModelState.Remove(nameof(model.Password));
            ViewData["ApiMessage"] =
                "The API accepted these credentials. MVC sign-in is not enabled yet, so you are not signed in.";
            return View(model);
        }

        return ShowLoginError(
            model,
            "Sign in could not be completed. Please try again later.");
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
