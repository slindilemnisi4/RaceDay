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
                    eventResponse.RegistrationDeadline))
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
            var response = await apiClient.GetAsync<List<ApiOrganiserEventSummaryResponse>>(
                "api/events/mine",
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Data is null)
            {
                logger.LogWarning(
                    "The API returned HTTP {StatusCode} while retrieving the organiser dashboard.",
                    (int)response.StatusCode);
                return View(
                    "OrganiserDashboard",
                    new OrganiserDashboardViewModel(
                        OrganiserPageState.Failure,
                        [],
                        "Your dashboard could not be loaded. Please try again."));
            }

            var events = response.Data
                .Select(item => new OrganiserEventViewModel(
                    item.EventID,
                    item.EventName,
                    item.Description,
                    item.EventDate,
                    item.Location,
                    item.DistanceKM,
                    item.EventType,
                    item.RegistrationDeadline,
                    item.CreatedAt,
                    item.TotalEnrolments))
                .ToList();

            return View(
                "OrganiserDashboard",
                new OrganiserDashboardViewModel(
                    events.Count == 0 ? OrganiserPageState.Empty : OrganiserPageState.Success,
                    events));
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
                    "The API response could not be read. Please try again."));
        }
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
