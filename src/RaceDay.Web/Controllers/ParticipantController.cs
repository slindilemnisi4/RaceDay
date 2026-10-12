using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaceDay.Web.Models.Events;
using RaceDay.Web.Models.Participant;
using RaceDay.Web.Services;

namespace RaceDay.Web.Controllers;

[Authorize(Roles = "Participant")]
public sealed class ParticipantController(
    RaceDayApiClient apiClient,
    ILogger<ParticipantController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> MyRaces(CancellationToken cancellationToken)
    {
        try
        {
            var enrolmentResponse = await apiClient.GetAsync<List<ApiEnrolmentResponse>>(
                "api/enrolments/me",
                cancellationToken);
            if (enrolmentResponse.StatusCode != HttpStatusCode.OK
                || enrolmentResponse.Data is null)
            {
                return ApiFailure(
                    enrolmentResponse.StatusCode,
                    "MyRaces",
                    "We couldn't load your races right now. Please try again.");
            }

            if (enrolmentResponse.Data.Count == 0)
            {
                return View(new MyRacesViewModel(ParticipantPageState.Empty, []));
            }

            var eventResponse = await apiClient.GetAsync<List<ApiEventResponse>>(
                "api/events",
                cancellationToken);
            if (eventResponse.StatusCode != HttpStatusCode.OK || eventResponse.Data is null)
            {
                return ApiFailure(
                    eventResponse.StatusCode,
                    "MyRaces",
                    "Your races were found, but event details could not be loaded.");
            }

            var categoryResponse = await apiClient.GetAsync<List<ApiCategoryResponse>>(
                "api/categories",
                cancellationToken);
            if (categoryResponse.StatusCode != HttpStatusCode.OK || categoryResponse.Data is null)
            {
                return ApiFailure(
                    categoryResponse.StatusCode,
                    "MyRaces",
                    "Your races were found, but category details could not be loaded.");
            }

            var events = eventResponse.Data.ToDictionary(item => item.EventID);
            var categories = categoryResponse.Data.ToDictionary(item => item.CategoryID);
            var races = enrolmentResponse.Data
                .Select(enrolment =>
                {
                    events.TryGetValue(enrolment.EventID, out var eventDetails);
                    categories.TryGetValue(enrolment.CategoryID, out var categoryDetails);
                    return new ParticipantRaceViewModel(
                        enrolment.EventName,
                        eventDetails?.EventDate,
                        eventDetails?.Location,
                        enrolment.CategoryName,
                        categoryDetails?.EntryFee,
                        eventDetails?.Status == 1,
                        enrolment.EntryDate);
                })
                .ToList();

            return View(new MyRacesViewModel(ParticipantPageState.Success, races));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            return ApiException(
                exception,
                "The request to load your races timed out. Please try again.",
                "MyRaces");
        }
        catch (HttpRequestException exception)
        {
            return ApiException(
                exception,
                "We couldn't reach RaceDay right now. Please try again.",
                "MyRaces");
        }
        catch (JsonException exception)
        {
            return ApiException(
                exception,
                "The API returned race data that could not be read.",
                "MyRaces");
        }
    }

    [HttpGet]
    public async Task<IActionResult> MyResults(CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.GetAsync<List<ApiResultResponse>>(
                "api/results/me",
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Data is null)
            {
                return ApiFailure(
                    response.StatusCode,
                    "MyResults",
                    "We couldn't load your results right now. Please try again.");
            }

            var results = response.Data
                .Select(result => new ParticipantResultViewModel(
                    result.EventName,
                    result.CategoryName,
                    result.FinishTime,
                    result.OverallPosition,
                    result.CategoryPosition,
                    result.ResultStatus))
                .ToList();

            return View(new MyResultsViewModel(
                results.Count == 0 ? ParticipantPageState.Empty : ParticipantPageState.Success,
                results));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            return ApiException(
                exception,
                "The request to load your results timed out. Please try again.",
                "MyResults");
        }
        catch (HttpRequestException exception)
        {
            return ApiException(
                exception,
                "We couldn't reach RaceDay right now. Please try again.",
                "MyResults");
        }
        catch (JsonException exception)
        {
            return ApiException(
                exception,
                "The API returned results that could not be read.",
                "MyResults");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.GetAsync<UserProfileApiResponse>(
                "api/users/me",
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Data is null)
            {
                return ApiFailure(
                    response.StatusCode,
                    "Profile",
                    "We couldn't load your profile right now. Please try again.");
            }

            return View(ToProfileViewModel(response.Data));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            return ApiException(
                exception,
                "The request to load your profile timed out. Please try again.",
                "Profile");
        }
        catch (HttpRequestException exception)
        {
            return ApiException(
                exception,
                "We couldn't reach RaceDay right now. Please try again.",
                "Profile");
        }
        catch (JsonException exception)
        {
            return ApiException(
                exception,
                "The API returned profile data that could not be read.",
                "Profile");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(
        ParticipantProfileViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Role = "Participant";
            return View(model);
        }

        try
        {
            var response = await apiClient.SendAuthenticatedAsync<
                UpdateProfileApiRequest,
                UserProfileApiResponse>(
                HttpMethod.Put,
                "api/users/me",
                new UpdateProfileApiRequest(model.FirstName, model.LastName, model.Email),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK && response.Data is not null)
            {
                await RefreshCookieIdentityAsync(response.Data);
                TempData["ParticipantMessage"] = "Your profile has been updated.";
                return RedirectToAction(nameof(Profile));
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                ModelState.AddModelError(
                    nameof(model.Email),
                    "That email address is already associated with another account.");
            }
            else if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Please check that all profile details are valid.");
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return RedirectToAction("Login", "Account", new { sessionExpired = true });
            }
            else
            {
                logger.LogWarning(
                    "The API returned HTTP {StatusCode} while updating a participant profile.",
                    (int)response.StatusCode);
                ModelState.AddModelError(
                    string.Empty,
                    "Your profile could not be updated. Please try again.");
            }

            model.Role = "Participant";
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API profile update request timed out.");
            ModelState.AddModelError(
                string.Empty,
                "The request timed out. Please check your profile before trying again.");
            model.Role = "Participant";
            return View(model);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API profile update request failed.");
            ModelState.AddModelError(
                string.Empty,
                "We couldn't reach RaceDay. Please try again later.");
            model.Role = "Participant";
            return View(model);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned an invalid profile update response.");
            ModelState.AddModelError(
                string.Empty,
                "The API response could not be read. Please reload your profile.");
            model.Role = "Participant";
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Join(
        int? eventID,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventResponse = await apiClient.GetAsync<List<ApiEventResponse>>(
                "api/events",
                cancellationToken);
            if (eventResponse.StatusCode != HttpStatusCode.OK || eventResponse.Data is null)
            {
                LogApiFailure(eventResponse.StatusCode, "Join Race events");
                return View(JoinFailure(
                    "We couldn't load events right now. Please try again."));
            }

            if (eventResponse.Data.Count == 0)
            {
                return View(new JoinRaceViewModel(JoinRacePageState.Empty, [], eventID));
            }

            var categoryResponse = await apiClient.GetAsync<List<ApiCategoryResponse>>(
                "api/categories",
                cancellationToken);
            if (categoryResponse.StatusCode != HttpStatusCode.OK || categoryResponse.Data is null)
            {
                LogApiFailure(categoryResponse.StatusCode, "Join Race categories");
                return View(JoinFailure(
                    "Category choices are temporarily unavailable. Please try again."));
            }

            var events = eventResponse.Data
                .Select(eventItem => new JoinRaceEventViewModel(
                    eventItem.EventID,
                    eventItem.EventName,
                    eventItem.EventDate,
                    eventItem.Location,
                    categoryResponse.Data
                        .Where(category => category.EventID == eventItem.EventID)
                        .Select(category => new EventCategoryViewModel(
                            category.CategoryID,
                            category.CategoryName,
                            category.DistanceKM,
                            category.EntryFee))
                        .ToList(),
                    eventItem.Status))
                .ToList();

            return View(new JoinRaceViewModel(
                JoinRacePageState.Success,
                events,
                events.Any(item => item.EventID == eventID) ? eventID : null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The Join Race data request timed out.");
            return View(JoinFailure("The request timed out. Please try again."));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The Join Race data request failed.");
            return View(JoinFailure("We couldn't reach RaceDay right now. Please try again."));
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned invalid data for Join Race.");
            return View(JoinFailure("The API response could not be read. Please try again."));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Join(
        JoinEventViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ParticipantMessage"] = "Choose a valid event category before joining.";
            return RedirectToAction(nameof(Join), new { eventID = model.EventID });
        }

        try
        {
            var response = await apiClient.SendAuthenticatedAsync<
                JoinEventApiRequest,
                ApiEnrolmentResponse>(
                HttpMethod.Post,
                $"api/events/{model.EventID}/enrolments",
                new JoinEventApiRequest(model.CategoryID),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created && response.Data is not null)
            {
                TempData["ParticipantMessage"] =
                    $"You joined {response.Data.EventName} in the {response.Data.CategoryName} category.";
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return RedirectToAction("Login", "Account", new { sessionExpired = true });
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return RedirectToAction("AccessDenied", "Account");
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                TempData["ParticipantError"] =
                    "That event or category could not be found. Refresh the page and try again.";
            }
            else if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                TempData["ParticipantError"] =
                    "The selected category is invalid. Choose a category from the list and try again.";
            }
            else if (response.StatusCode == HttpStatusCode.Conflict)
            {
                TempData["ParticipantError"] = GetConflictMessage(response.ErrorContent);
            }
            else
            {
                logger.LogWarning(
                    "The API returned HTTP {StatusCode} while creating an event enrolment.",
                    (int)response.StatusCode);
                TempData["ParticipantError"] =
                    "Joining this event is temporarily unavailable. Please try again.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API event-enrolment request timed out.");
            TempData["ParticipantError"] =
                "The request timed out. Check My Races before trying again.";
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API event-enrolment request failed.");
            TempData["ParticipantError"] =
                "Joining this event is temporarily unavailable. Please try again.";
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned an invalid event-enrolment response.");
            TempData["ParticipantError"] =
                "The API response could not be read. Check My Races before trying again.";
        }

        return RedirectToAction(nameof(Join), new { eventID = model.EventID });
    }

    private IActionResult ApiFailure(
        HttpStatusCode statusCode,
        string viewName,
        string message)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            message = "Your sign-in session was rejected. Please sign in again.";
        }
        else if (statusCode == HttpStatusCode.Forbidden)
        {
            message = "Your account does not have permission to view this information.";
        }
        else
        {
            LogApiFailure(statusCode, viewName);
        }

        ViewData["ApiMessage"] = message;
        return viewName switch
        {
            "MyRaces" => View(viewName, new MyRacesViewModel(ParticipantPageState.Failure, [], message)),
            "MyResults" => View(viewName, new MyResultsViewModel(ParticipantPageState.Failure, [], message)),
            "Profile" => View(viewName, new ParticipantProfileViewModel()),
            _ => throw new InvalidOperationException("Unsupported participant page.")
        };
    }

    private void LogApiFailure(HttpStatusCode statusCode, string operation)
    {
        if ((int)statusCode >= 500)
        {
            logger.LogError(
                "The API returned HTTP {StatusCode} while loading participant data for {Operation}.",
                (int)statusCode,
                operation);
        }
        else
        {
            logger.LogWarning(
                "The API returned HTTP {StatusCode} while loading participant data for {Operation}.",
                (int)statusCode,
                operation);
        }
    }

    private IActionResult ApiException(
        Exception exception,
        string message,
        string viewName)
    {
        logger.LogError(exception, "Failed to load participant page {Page}.", viewName);
        ViewData["ApiMessage"] = message;
        return viewName switch
        {
            "MyRaces" => View(viewName, new MyRacesViewModel(ParticipantPageState.Failure, [], message)),
            "MyResults" => View(viewName, new MyResultsViewModel(ParticipantPageState.Failure, [], message)),
            "Profile" => View(viewName, new ParticipantProfileViewModel()),
            _ => throw new InvalidOperationException("Unsupported participant page.")
        };
    }

    private async Task RefreshCookieIdentityAsync(UserProfileApiResponse profile)
    {
        var ticket = await HttpContext.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        var claims = User.Claims
            .Where(claim => claim.Type is not ClaimTypes.Name and not ClaimTypes.Email)
            .ToList();
        claims.Add(new Claim(ClaimTypes.Name, $"{profile.FirstName} {profile.LastName}".Trim()));
        claims.Add(new Claim(ClaimTypes.Email, profile.Email));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme)),
            ticket.Properties);
    }

    private static ParticipantProfileViewModel ToProfileViewModel(
        UserProfileApiResponse profile) =>
        new()
        {
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Email = profile.Email,
            Role = profile.Role == 1 ? "Participant" : "Organiser",
            CreatedAt = profile.CreatedAt
        };

    private static JoinRaceViewModel JoinFailure(string message) =>
        new(JoinRacePageState.Failure, [], Message: message);

    private static string GetConflictMessage(string? errorContent)
    {
        if (!string.IsNullOrWhiteSpace(errorContent))
        {
            try
            {
                using var document = JsonDocument.Parse(errorContent);
                if (document.RootElement.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString() switch
                    {
                        "The participant is already enrolled in this category." =>
                            "You are already enrolled in this category.",
                        "Registration for this event is closed." =>
                            "Registration for this event is closed.",
                        "This category is full." =>
                            "This category is full.",
                        _ => "You could not join this category. Please choose another category or check My Races."
                    };
                }
            }
            catch (JsonException)
            {
                return "You could not join this category. Please choose another category or check My Races.";
            }
        }

        return "You could not join this category. Please choose another category or check My Races.";
    }

    private sealed record UpdateProfileApiRequest(string FirstName, string LastName, string Email);

    private sealed record JoinEventApiRequest(int CategoryID);
}
