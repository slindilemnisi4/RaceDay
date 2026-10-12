using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaceDay.Web.Models;
using RaceDay.Web.Models.Events;
using RaceDay.Web.Models.Organiser;
using RaceDay.Web.Services;

namespace RaceDay.Web.Controllers;

public class HomeController(
    RaceDayApiClient apiClient,
    ILogger<HomeController> logger) : Controller
{
    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View(EventDiscoveryViewModel.PublicLanding);
        }

        if (User.IsInRole("Organiser"))
        {
            return await OrganiserDashboardAsync(cancellationToken);
        }

        try
        {
            var response = await apiClient.GetAsync<List<ApiEventResponse>>(
                "api/events",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return View(EventDiscoveryViewModel.Failure);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                logger.LogWarning("The API denied access to the event list.");
                return View(EventDiscoveryViewModel.Forbidden);
            }

            if ((int)response.StatusCode is < 200 or >= 300)
            {
                var statusCode = (int)response.StatusCode;
                if (statusCode >= 500)
                {
                    logger.LogError(
                        "The API returned HTTP {StatusCode} while retrieving events.",
                        statusCode);
                }
                else
                {
                    logger.LogWarning(
                        "The API returned HTTP {StatusCode} while retrieving events.",
                        statusCode);
                }

                return View(EventDiscoveryViewModel.Failure);
            }

            if (response.Data is null)
            {
                logger.LogError("The API returned an empty or null event-list response.");
                return View(EventDiscoveryViewModel.Failure);
            }

            var events = response.Data
                .Select(eventResponse => new EventCardViewModel(
                    eventResponse.EventID,
                    eventResponse.EventName,
                    eventResponse.Description,
                    eventResponse.EventDate,
                    eventResponse.Location,
                    eventResponse.DistanceKM,
                    eventResponse.EventType,
                    eventResponse.RegistrationDeadline,
                    eventResponse.EntryFee,
                    eventResponse.Status))
                .ToList();

            return View(events.Count == 0
                ? EventDiscoveryViewModel.Empty
                : EventDiscoveryViewModel.Success(events));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API event-list request timed out.");
            return View(EventDiscoveryViewModel.Failure);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API event-list request failed.");
            return View(EventDiscoveryViewModel.Failure);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned an invalid event-list response.");
            return View(EventDiscoveryViewModel.Failure);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "An unexpected error occurred while retrieving events.");
            return View(EventDiscoveryViewModel.Failure);
        }
    }

    private async Task<IActionResult> OrganiserDashboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            var platformResponse = await apiClient.GetAsync<List<ApiEventResponse>>(
                "api/events",
                cancellationToken);
            var ownerResponse = await apiClient.GetAsync<List<ApiOrganiserEventSummaryResponse>>(
                "api/events/mine",
                cancellationToken);

            var platformEvents = platformResponse.StatusCode == HttpStatusCode.OK
                ? platformResponse.Data?.Select(item => new EventCardViewModel(
                    item.EventID,
                    item.EventName,
                    item.Description,
                    item.EventDate,
                    item.Location,
                    item.DistanceKM,
                    item.EventType,
                    item.RegistrationDeadline,
                    item.EntryFee,
                    item.Status)).ToList() ?? []
                : [];
            var ownEvents = ownerResponse.StatusCode == HttpStatusCode.OK
                ? ownerResponse.Data?.Select(ToOrganiserEventViewModel).ToList() ?? []
                : [];

            if (platformResponse.StatusCode != HttpStatusCode.OK || platformResponse.Data is null)
            {
                logger.LogWarning(
                    "The API returned HTTP {StatusCode} while retrieving RaceDay-wide events for the organiser dashboard.",
                    (int)platformResponse.StatusCode);
            }

            if (ownerResponse.StatusCode != HttpStatusCode.OK || ownerResponse.Data is null)
            {
                logger.LogWarning(
                    "The API returned HTTP {StatusCode} while retrieving organiser-owned dashboard events.",
                    (int)ownerResponse.StatusCode);
            }

            return View(
                "OrganiserDashboard",
                new OrganiserDashboardViewModel(
                    ownerResponse.StatusCode != HttpStatusCode.OK || ownerResponse.Data is null
                        ? OrganiserPageState.Failure
                        : ownEvents.Count == 0 ? OrganiserPageState.Empty : OrganiserPageState.Success,
                    ownEvents,
                    platformResponse.StatusCode != HttpStatusCode.OK || platformResponse.Data is null
                        ? OrganiserPageState.Failure
                        : platformEvents.Count == 0 ? OrganiserPageState.Empty : OrganiserPageState.Success,
                    platformEvents,
                    ownerResponse.StatusCode == HttpStatusCode.OK
                        ? null
                        : "Your event summary could not be loaded. Please try again.",
                    platformResponse.StatusCode == HttpStatusCode.OK
                        ? null
                        : "RaceDay-wide events could not be loaded. Please try again."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The organiser dashboard request timed out.");
            return View(
                "OrganiserDashboard",
                new OrganiserDashboardViewModel(
                    OrganiserPageState.Failure,
                    [],
                    OrganiserPageState.Failure,
                    [],
                    "The request timed out. Please try again.",
                    "The request timed out. Please try again."));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The organiser dashboard request failed.");
            return View(
                "OrganiserDashboard",
                new OrganiserDashboardViewModel(
                    OrganiserPageState.Failure,
                    [],
                    OrganiserPageState.Failure,
                    [],
                    "We couldn't reach RaceDay. Please try again later.",
                    "We couldn't reach RaceDay. Please try again later."));
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned invalid organiser dashboard data.");
            return View(
                "OrganiserDashboard",
                new OrganiserDashboardViewModel(
                    OrganiserPageState.Failure,
                    [],
                    OrganiserPageState.Failure,
                    [],
                    "The API response could not be read. Please try again.",
                    "The API response could not be read. Please try again."));
        }
    }

    private static OrganiserEventViewModel ToOrganiserEventViewModel(
        ApiOrganiserEventSummaryResponse item) =>
        new(
            item.EventID,
            item.EventName,
            item.Description,
            item.EventDate,
            item.Location,
            item.DistanceKM,
            item.EntryFee,
            item.EventType,
            item.Status,
            item.RegistrationDeadline,
            item.CreatedAt,
            item.TotalEnrolments);

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
