using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RaceDay.Web.Models.Events;
using RaceDay.Web.Models.Organiser;
using RaceDay.Web.Models.Participant;
using RaceDay.Web.Services;

namespace RaceDay.Web.Controllers;

[Authorize(Roles = "Organiser")]
public sealed class OrganiserController(
    RaceDayApiClient apiClient,
    ILogger<OrganiserController> logger,
    EventMediaStorageService mediaStorage,
    IOptions<EventMediaStorageOptions> mediaOptions) : Controller
{
    [HttpGet]
    public async Task<IActionResult> MyEvents(CancellationToken cancellationToken)
    {
        var (events, error) = await LoadEventsAsync(cancellationToken);
        if (events is null)
        {
            return View(new OrganiserEventsPageViewModel(OrganiserPageState.Failure, [], error));
        }

        return View(new OrganiserEventsPageViewModel(
            events.Count == 0 ? OrganiserPageState.Empty : OrganiserPageState.Success,
            events));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var (events, error) = await LoadEventsAsync(cancellationToken);
        if (events is null)
        {
            return View(new OrganiserEventDetailsViewModel(
                EmptyEvent(id),
                [],
                error));
        }

        var eventItem = events.FirstOrDefault(item => item.EventID == id);
        if (eventItem is null)
        {
            return NotFound();
        }

        var (categories, categoryError) = await LoadCategoriesAsync(cancellationToken);
        var eventCategories = categories?
            .Where(category => category.EventID == id)
            .Select(ToCategoryViewModel)
            .ToList() ?? [];

        return View(new OrganiserEventDetailsViewModel(
            eventItem,
            eventCategories,
            categoryError));
    }

    [HttpGet]
    public IActionResult CreateEvent() => View(new OrganiserEventFormViewModel
    {
        EventDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        RegistrationDeadline = DateOnly.FromDateTime(DateTime.Today.AddDays(14))
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEvent(
        OrganiserEventFormViewModel model,
        CancellationToken cancellationToken)
    {
        ValidateEventDates(model);
        ValidateEntryFee(model.EntryFee, nameof(model.EntryFee));
        ValidateMediaUploadCounts(model, existingMedia: []);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var storedMedia = new List<StoredEventMedia>();
        try
        {
            try
            {
                await StoreNewMediaAsync(model, storedMedia, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await CleanupStoredMediaAsync(storedMedia, "event media storage failed during event creation");
                logger.LogError(exception, "Could not store event media while creating an event.");
                ModelState.AddModelError(string.Empty, "Event media could not be stored. Please try again.");
                return View(model);
            }

            var response = await apiClient.SendAuthenticatedAsync<object, ApiEventResponse>(
                HttpMethod.Post,
                "api/events",
                new
                {
                    model.EventName,
                    model.Description,
                    model.EventDate,
                    model.Location,
                    model.DistanceKM,
                    model.EntryFee,
                    EventType = model.EventType,
                    model.RegistrationDeadline,
                    Media = storedMedia
                },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created && response.Data is not null)
            {
                TempData["OrganiserMessage"] = "The event and its General Entry category were created.";
                return RedirectToAction(nameof(Details), new { id = response.Data.EventID });
            }

            await CleanupStoredMediaAsync(storedMedia, "event creation was rejected");
            AddFormApiError(response.StatusCode, "The event could not be created. Check the details and try again.");
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupStoredMediaAsync(storedMedia, "event creation was cancelled");
            throw;
        }
        catch (EventMediaValidationException exception)
        {
            await CleanupStoredMediaAsync(storedMedia, "media validation failed during event creation");
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(model);
        }
        catch (HttpRequestException exception)
        {
            LogUncertainMediaOutcome(storedMedia, "event creation request failed");
            return EventRequestFailure(exception, model);
        }
        catch (TaskCanceledException exception)
        {
            LogUncertainMediaOutcome(storedMedia, "event creation request timed out");
            return EventRequestFailure(exception, model, timedOut: true);
        }
        catch (JsonException exception)
        {
            LogUncertainMediaOutcome(storedMedia, "event creation response could not be read");
            logger.LogError(exception, "The API returned invalid data after event creation.");
            ModelState.AddModelError(string.Empty, "The API response could not be read. Check My Events before retrying.");
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditEvent(int id, CancellationToken cancellationToken)
    {
        var (events, error) = await LoadEventsAsync(cancellationToken);
        if (events is null)
        {
            TempData["OrganiserError"] = error;
            return RedirectToAction(nameof(MyEvents));
        }

        var eventItem = events.FirstOrDefault(item => item.EventID == id);
        if (eventItem is null)
        {
            return NotFound();
        }

        if (eventItem.IsCancelled)
        {
            TempData["OrganiserError"] = "Cancelled events cannot be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(new OrganiserEventFormViewModel
        {
            EventID = eventItem.EventID,
            EventName = eventItem.EventName,
            Description = eventItem.Description,
            EventDate = eventItem.EventDate,
            Location = eventItem.Location,
            DistanceKM = eventItem.DistanceKM,
            EntryFee = eventItem.EntryFee ?? 0,
            EventType = eventItem.EventType,
            RegistrationDeadline = eventItem.RegistrationDeadline,
            ExistingMedia = eventItem.Media ?? [],
            RetainedMediaIds = eventItem.Media?
                .Where(media => !media.IsVideo)
                .Select(media => media.EventMediaID)
                .ToList() ?? []
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEvent(
        int id,
        OrganiserEventFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.EventID = id;
        ValidateEventDates(model);
        ValidateEntryFee(model.EntryFee, nameof(model.EntryFee));
        var (ownedEvents, loadError) = await LoadEventsAsync(cancellationToken);
        if (ownedEvents is null)
        {
            TempData["OrganiserError"] = loadError;
            return RedirectToAction(nameof(MyEvents));
        }

        var existingEvent = ownedEvents.FirstOrDefault(item => item.EventID == id);
        if (existingEvent is null)
        {
            return NotFound();
        }

        model.ExistingMedia = existingEvent.Media ?? [];
        ValidateRetainedMedia(model, existingEvent.Media ?? []);
        ValidateMediaUploadCounts(model, existingEvent.Media ?? []);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var storedMedia = new List<StoredEventMedia>();
        try
        {
            var retainedMedia = (existingEvent.Media ?? [])
                .Where(media => media.MediaType == 1
                    ? model.RetainExistingVideo
                    : model.RetainedMediaIds.Contains(media.EventMediaID))
                .OrderBy(media => media.DisplayOrder)
                .ToList();
            try
            {
                await StoreNewMediaAsync(
                    model,
                    storedMedia,
                    cancellationToken,
                    startOrder: retainedMedia.Count);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await CleanupStoredMediaAsync(storedMedia, "event media storage failed during event update");
                logger.LogError(exception, "Could not store event media while updating event {EventID}.", id);
                ModelState.AddModelError(string.Empty, "Event media could not be stored. Please try again.");
                return View(model);
            }

            var requestedMedia = retainedMedia.Cast<object>()
                .Concat(storedMedia.Cast<object>())
                .ToList();
            var response = await apiClient.SendAuthenticatedAsync<object, ApiEventResponse>(
                HttpMethod.Put,
                $"api/events/{id}",
                new
                {
                    model.EventName,
                    model.Description,
                    model.EventDate,
                    model.Location,
                    model.DistanceKM,
                    model.EntryFee,
                    EventType = model.EventType,
                    model.RegistrationDeadline,
                    Media = requestedMedia
                },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK && response.Data is not null)
            {
                var retainedPaths = retainedMedia
                    .Select(media => media.RelativePath)
                    .ToHashSet(StringComparer.Ordinal);
                var removedPaths = (existingEvent.Media ?? [])
                    .Where(media => !retainedPaths.Contains(media.RelativePath))
                    .Select(media => media.RelativePath)
                    .ToList();
                await CleanupUnreferencedMediaAsync(removedPaths, cancellationToken);
                TempData["OrganiserMessage"] = "The event details were updated.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await CleanupStoredMediaAsync(storedMedia, "event update was rejected");
            AddFormApiError(response.StatusCode, "The event could not be updated. Check the details and try again.");
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupStoredMediaAsync(storedMedia, "event update was cancelled");
            throw;
        }
        catch (EventMediaValidationException exception)
        {
            await CleanupStoredMediaAsync(storedMedia, "media validation failed during event update");
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(model);
        }
        catch (HttpRequestException exception)
        {
            LogUncertainMediaOutcome(storedMedia, "event update request failed");
            return EventRequestFailure(exception, model);
        }
        catch (TaskCanceledException exception)
        {
            LogUncertainMediaOutcome(storedMedia, "event update request timed out");
            return EventRequestFailure(exception, model, timedOut: true);
        }
        catch (JsonException exception)
        {
            LogUncertainMediaOutcome(storedMedia, "event update response could not be read");
            logger.LogError(exception, "The API returned invalid data after event update.");
            ModelState.AddModelError(string.Empty, "The API response could not be read. Check My Events before retrying.");
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelEvent(int id, CancellationToken cancellationToken)
    {
        var response = await SendCancelAsync(id, cancellationToken);
        if (response is null)
        {
            TempData["OrganiserMessage"] = "The event was cancelled. Existing enrolments and results were kept.";
            return RedirectToAction(nameof(MyEvents));
        }

        TempData["OrganiserError"] = response;
        return RedirectToAction(nameof(MyEvents));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEvent(int id, CancellationToken cancellationToken)
    {
        try
        {
            var (events, loadError) = await LoadEventsAsync(cancellationToken);
            var mediaPaths = events?
                .FirstOrDefault(item => item.EventID == id)?
                .Media?
                .Select(media => media.RelativePath)
                .ToList() ?? [];
            if (events is null)
            {
                logger.LogWarning(
                    "Could not load event media before permanent deletion of event {EventID}: {Error}",
                    id,
                    loadError);
            }

            var response = await apiClient.DeleteAuthenticatedAsync<object>(
                $"api/events/{id}",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                await CleanupUnreferencedMediaAsync(mediaPaths, cancellationToken);
                TempData["OrganiserMessage"] =
                    "The event and its enrolments and results were permanently deleted.";
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return RedirectToAction("Login", "Account", new { sessionExpired = true });
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                TempData["OrganiserError"] = "You can only delete your own events.";
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                TempData["OrganiserError"] = "That event no longer exists.";
            }
            else
            {
                LogApiFailure(response.StatusCode, "permanent event deletion");
                TempData["OrganiserError"] =
                    "The event could not be permanently deleted. Please refresh and try again.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API request to permanently delete event {EventID} failed.", id);
            TempData["OrganiserError"] =
                "We couldn't reach RaceDay. The event was not confirmed as deleted.";
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API request to permanently delete event {EventID} timed out.", id);
            TempData["OrganiserError"] =
                "The request timed out. Check My Events before trying again.";
        }

        return RedirectToAction(nameof(MyEvents));
    }

    [HttpGet]
    public async Task<IActionResult> CreateCategory(int eventID, CancellationToken cancellationToken)
    {
        var ownedEvent = await FindOwnedEventAsync(eventID, cancellationToken);
        if (ownedEvent is null)
        {
            return NotFound();
        }

        if (ownedEvent.IsCancelled)
        {
            TempData["OrganiserError"] = "Categories cannot be changed for a cancelled event.";
            return RedirectToAction(nameof(Details), new { id = eventID });
        }

        return View(new OrganiserCategoryFormViewModel { EventID = eventID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(
        OrganiserCategoryFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!await EventIsOwnedAsync(model.EventID, cancellationToken))
        {
            return NotFound();
        }

        ValidateEntryFee(model.EntryFee, nameof(model.EntryFee));
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var response = await apiClient.SendAuthenticatedAsync<object, ApiOrganiserCategoryResponse>(
                HttpMethod.Post,
                "api/categories",
                new
                {
                    model.EventID,
                    model.CategoryName,
                    model.DistanceKM,
                    model.MaxParticipants,
                    model.EntryFee
                },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                TempData["OrganiserMessage"] = "The category was added.";
                return RedirectToAction(nameof(Details), new { id = model.EventID });
            }

            AddFormApiError(response.StatusCode, "The category could not be added. Check the details and try again.");
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return CategoryRequestFailure(exception, model);
        }
        catch (TaskCanceledException exception)
        {
            return CategoryRequestFailure(exception, model, timedOut: true);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditCategory(int id, CancellationToken cancellationToken)
    {
        var (categories, error) = await LoadCategoriesAsync(cancellationToken);
        if (categories is null)
        {
            TempData["OrganiserError"] = error;
            return RedirectToAction(nameof(MyEvents));
        }

        var category = categories.FirstOrDefault(item => item.CategoryID == id);
        if (category is null || !await EventIsOwnedAsync(category.EventID, cancellationToken))
        {
            return NotFound();
        }

        if (await FindOwnedEventAsync(category.EventID, cancellationToken) is { IsCancelled: true })
        {
            TempData["OrganiserError"] = "Categories cannot be changed for a cancelled event.";
            return RedirectToAction(nameof(Details), new { id = category.EventID });
        }

        return View(new OrganiserCategoryFormViewModel
        {
            CategoryID = category.CategoryID,
            EventID = category.EventID,
            CategoryName = category.CategoryName,
            DistanceKM = category.DistanceKM,
            MaxParticipants = category.MaxParticipants,
            EntryFee = category.EntryFee
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(
        int id,
        OrganiserCategoryFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.CategoryID = id;
        var (categories, error) = await LoadCategoriesAsync(cancellationToken);
        if (categories is null)
        {
            ModelState.AddModelError(string.Empty, error ?? "Categories could not be loaded.");
            return View(model);
        }

        var existing = categories.FirstOrDefault(item => item.CategoryID == id);
        if (existing is null || existing.EventID != model.EventID
            || !await EventIsOwnedAsync(existing.EventID, cancellationToken))
        {
            return NotFound();
        }

        if (string.Equals(existing.CategoryName, "General Entry", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.CategoryName), "The General Entry category is managed with the event.");
            return View(model);
        }

        ValidateEntryFee(model.EntryFee, nameof(model.EntryFee));
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var response = await apiClient.SendAuthenticatedAsync<object, ApiOrganiserCategoryResponse>(
                HttpMethod.Put,
                $"api/categories/{id}",
                new
                {
                    model.CategoryName,
                    model.DistanceKM,
                    model.MaxParticipants,
                    model.EntryFee
                },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                TempData["OrganiserMessage"] = "The category was updated.";
                return RedirectToAction(nameof(Details), new { id = model.EventID });
            }

            AddFormApiError(response.StatusCode, "The category could not be updated. Check the details and try again.");
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return CategoryRequestFailure(exception, model);
        }
        catch (TaskCanceledException exception)
        {
            return CategoryRequestFailure(exception, model, timedOut: true);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(
        int id,
        int eventID,
        CancellationToken cancellationToken)
    {
        var (categories, error) = await LoadCategoriesAsync(cancellationToken);
        if (categories is null)
        {
            TempData["OrganiserError"] = error;
            return RedirectToAction(nameof(Details), new { id = eventID });
        }

        var category = categories.FirstOrDefault(item => item.CategoryID == id);
        if (category is null || category.EventID != eventID
            || !await EventIsOwnedAsync(eventID, cancellationToken))
        {
            return NotFound();
        }

        var failure = await SendDeleteAsync($"api/categories/{id}", "category", cancellationToken);
        if (failure is null)
        {
            TempData["OrganiserMessage"] = "The category was deleted.";
        }
        else
        {
            TempData["OrganiserError"] = failure;
        }

        return RedirectToAction(nameof(Details), new { id = eventID });
    }

    [HttpGet]
    public async Task<IActionResult> Enrolments(
        int? eventID,
        CancellationToken cancellationToken)
    {
        var (events, eventError) = await LoadEventsAsync(cancellationToken);
        if (events is null)
        {
            return View(ResultsPage(OrganiserPageState.Failure, [], null, [], [], new Dictionary<int, int>(), eventError));
        }

        if (events.Count == 0)
        {
            return View(ResultsPage(OrganiserPageState.Empty, events, null, [], [], new Dictionary<int, int>()));
        }

        if (!eventID.HasValue)
        {
            return View(ResultsPage(OrganiserPageState.Success, events, null, [], [], new Dictionary<int, int>()));
        }

        if (!events.Any(item => item.EventID == eventID.Value))
        {
            return NotFound();
        }

        try
        {
            var enrolmentResponse = await apiClient.GetAsync<List<ApiOrganiserEnrolmentResponse>>(
                $"api/events/{eventID.Value}/enrolments",
                cancellationToken);
            if (enrolmentResponse.StatusCode != HttpStatusCode.OK || enrolmentResponse.Data is null)
            {
                return View(ResultsPage(
                    OrganiserPageState.Failure,
                    events,
                    eventID,
                    [],
                    [],
                    new Dictionary<int, int>(),
                    "Enrolments could not be loaded. Please try again."));
            }

            var resultResponse = await apiClient.GetAsync<List<ApiOrganiserResultResponse>>(
                $"api/events/{eventID.Value}/results",
                cancellationToken);
            if (resultResponse.StatusCode != HttpStatusCode.OK || resultResponse.Data is null)
            {
                return View(ResultsPage(
                    OrganiserPageState.Failure,
                    events,
                    eventID,
                    [],
                    [],
                    new Dictionary<int, int>(),
                    "Results could not be loaded. Please try again."));
            }

            var results = resultResponse.Data
                .Select(item => new ParticipantResultViewModel(
                    item.EventName,
                    item.EventType,
                    item.CategoryName,
                    item.FinishTime,
                    item.OverallPosition,
                    item.CategoryPosition,
                    item.ResultStatus,
                    item.EventID,
                    events
                        .FirstOrDefault(eventItem => eventItem.EventID == item.EventID)?.Media))
                .ToList();
            var resultIdsByEntry = resultResponse.Data
                .ToDictionary(item => item.EntryID, item => item.ResultID);
            var enrolments = enrolmentResponse.Data
                .Select(item => new OrganiserEnrolmentViewModel(
                    item.EntryID,
                    item.CategoryID,
                    item.CategoryName,
                    item.EntryDate))
                .ToList();

            return View(ResultsPage(
                enrolments.Count == 0 ? OrganiserPageState.Empty : OrganiserPageState.Success,
                events,
                eventID,
                enrolments,
                results,
                resultIdsByEntry));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API enrolment/results request failed for an organiser event.");
            return View(ResultsPage(OrganiserPageState.Failure, events, eventID, [], [], new Dictionary<int, int>(),
                "We couldn't reach RaceDay. Please try again."));
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API enrolment/results request timed out for an organiser event.");
            return View(ResultsPage(OrganiserPageState.Failure, events, eventID, [], [], new Dictionary<int, int>(),
                "The request timed out. Please try again."));
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned invalid organiser enrolment/result data.");
            return View(ResultsPage(OrganiserPageState.Failure, events, eventID, [], [], new Dictionary<int, int>(),
                "The API response could not be read. Please try again."));
        }
    }

    [HttpGet]
    public async Task<IActionResult> CreateResult(
        int eventID,
        int entryID,
        CancellationToken cancellationToken)
    {
        var enrolment = await FindOwnedEnrolmentAsync(eventID, entryID, cancellationToken);
        if (enrolment is null)
        {
            return NotFound();
        }

        return View(new OrganiserResultFormViewModel
        {
            EventID = eventID,
            EntryID = entryID,
            OverallPosition = 1,
            CategoryPosition = 1
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateResult(
        OrganiserResultFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!await IsValidEntryForEventAsync(model.EventID, model.EntryID, cancellationToken))
        {
            return NotFound();
        }

        ValidateResult(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var response = await apiClient.SendAuthenticatedAsync<object, ApiOrganiserResultResponse>(
                HttpMethod.Post,
                $"api/entries/{model.EntryID}/results",
                new
                {
                    model.EntryID,
                    model.FinishTime,
                    model.OverallPosition,
                    model.CategoryPosition,
                    ResultStatus = model.ResultStatus
                },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                TempData["OrganiserMessage"] = "The result was recorded.";
                return RedirectToAction(nameof(Enrolments), new { eventID = model.EventID });
            }

            AddFormApiError(response.StatusCode, "The result could not be recorded. Check the details and try again.");
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return ResultRequestFailure(exception, model);
        }
        catch (TaskCanceledException exception)
        {
            return ResultRequestFailure(exception, model, timedOut: true);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditResult(int eventID, int resultID, CancellationToken cancellationToken)
    {
        var result = await FindOwnedResultAsync(eventID, resultID, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        return View(new OrganiserResultFormViewModel
        {
            EventID = eventID,
            EntryID = result.EntryID,
            ResultID = result.ResultID,
            FinishTime = result.FinishTime,
            OverallPosition = result.OverallPosition,
            CategoryPosition = result.CategoryPosition,
            ResultStatus = result.ResultStatus
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditResult(
        int eventID,
        int resultID,
        OrganiserResultFormViewModel model,
        CancellationToken cancellationToken)
    {
        var existing = await FindOwnedResultAsync(eventID, resultID, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        model.EventID = eventID;
        model.EntryID = existing.EntryID;
        model.ResultID = resultID;
        ValidateResult(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var response = await apiClient.SendAuthenticatedAsync<object, ApiOrganiserResultResponse>(
                HttpMethod.Put,
                $"api/results/{resultID}",
                new
                {
                    model.FinishTime,
                    model.OverallPosition,
                    model.CategoryPosition,
                    ResultStatus = model.ResultStatus
                },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                TempData["OrganiserMessage"] = "The result was updated.";
                return RedirectToAction(nameof(Enrolments), new { eventID });
            }

            AddFormApiError(response.StatusCode, "The result could not be updated. Check the details and try again.");
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return ResultRequestFailure(exception, model);
        }
        catch (TaskCanceledException exception)
        {
            return ResultRequestFailure(exception, model, timedOut: true);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.GetAsync<ApiOrganiserProfileResponse>(
                "api/users/me",
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Data is null)
            {
                LogApiFailure(response.StatusCode, "organiser profile");
                ViewData["ApiMessage"] = "Your profile could not be loaded. Please try again.";
                return View("Profile", new OrganiserProfileViewModel());
            }

            return View(new OrganiserProfileViewModel
            {
                FirstName = response.Data.FirstName,
                LastName = response.Data.LastName,
                Email = response.Data.Email,
                Role = response.Data.Role == 0 ? "Organiser" : "Participant",
                CreatedAt = response.Data.CreatedAt
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API profile request failed for an organiser.");
            ViewData["ApiMessage"] = "We couldn't reach RaceDay. Please try again later.";
            return View("Profile", new OrganiserProfileViewModel());
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API profile request timed out for an organiser.");
            ViewData["ApiMessage"] = "The request timed out. Please try again.";
            return View("Profile", new OrganiserProfileViewModel());
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned invalid profile data for an organiser.");
            ViewData["ApiMessage"] = "The API response could not be read. Please try again.";
            return View("Profile", new OrganiserProfileViewModel());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(
        OrganiserProfileViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Role = "Organiser";
            return View(model);
        }

        try
        {
            var response = await apiClient.SendAuthenticatedAsync<object, ApiOrganiserProfileResponse>(
                HttpMethod.Put,
                "api/users/me",
                new { model.FirstName, model.LastName, model.Email },
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                TempData["OrganiserMessage"] = "Your profile was updated.";
                return RedirectToAction(nameof(Profile));
            }

            AddFormApiError(response.StatusCode, "Your profile could not be updated. Please try again.");
            model.Role = "Organiser";
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API profile update request failed for an organiser.");
            ModelState.AddModelError(string.Empty, "We couldn't reach RaceDay. Your changes were not saved.");
            model.Role = "Organiser";
            return View(model);
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API profile update request timed out for an organiser.");
            ModelState.AddModelError(string.Empty, "The request timed out. Check your profile before retrying.");
            model.Role = "Organiser";
            return View(model);
        }
    }

    private async Task<(List<OrganiserEventViewModel>? Events, string? Error)> LoadEventsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.GetAsync<List<ApiOrganiserEventSummaryResponse>>(
                "api/events/mine",
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Data is null)
            {
                LogApiFailure(response.StatusCode, "organiser events");
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return (
                        null,
                        response.StatusCode == HttpStatusCode.Unauthorized
                            ? "Your sign-in session was rejected. Sign in again."
                            : "Your account does not have permission to view organiser events.");
                }

                return await LoadOwnedEventsFromEventListAsync(
                    response.StatusCode,
                    cancellationToken);
            }

            return (response.Data.Select(ToEventViewModel).ToList(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API request for organiser events failed.");
            return (null, "We couldn't reach RaceDay. Please try again later.");
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API request for organiser events timed out.");
            return (null, "The request timed out. Please try again.");
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned invalid organiser event data.");
            return (null, "The API response could not be read. Please try again.");
        }
    }

    private async Task<(List<OrganiserEventViewModel>? Events, string? Error)>
        LoadOwnedEventsFromEventListAsync(
            HttpStatusCode organiserEventsStatusCode,
            CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue("UserID")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId) || userId <= 0)
        {
            logger.LogWarning("The organiser identity did not contain a valid user ID for event filtering.");
            return (null, "Your organiser identity could not be verified. Sign in again and retry.");
        }

        try
        {
            var response = await apiClient.GetAsync<List<ApiEventResponse>>(
                "api/events",
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Data is null)
            {
                LogApiFailure(response.StatusCode, "fallback organiser event list");
                return (null, "Your events could not be loaded. Please try again.");
            }

            logger.LogWarning(
                "Loaded organiser-owned events from the general event endpoint after the organiser event endpoint returned HTTP {StatusCode}.",
                (int)organiserEventsStatusCode);

            return (
                response.Data
                    .Where(item => item.OrganizerID == userId)
                    .Select(item => new OrganiserEventViewModel(
                        item.EventID,
                        item.EventName,
                        item.Description,
                        item.EventDate,
                        item.Location,
                        item.DistanceKM,
                        item.EntryFee,
                        item.MinimumEntryFee ?? item.EntryFee,
                        item.MaximumEntryFee ?? item.EntryFee,
                        item.EventType,
                        item.Status,
                        item.RegistrationDeadline,
                        default,
                        null,
                        EventMediaMapping.ToViewModels(item.Media)))
                    .ToList(),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The fallback API request for organiser-owned events failed.");
            return (null, "We couldn't reach RaceDay. Please try again later.");
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The fallback API request for organiser-owned events timed out.");
            return (null, "The request timed out. Please try again.");
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The fallback API returned invalid event data.");
            return (null, "The API response could not be read. Please try again.");
        }
    }

    private async Task<(List<ApiOrganiserCategoryResponse>? Categories, string? Error)> LoadCategoriesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.GetAsync<List<ApiOrganiserCategoryResponse>>(
                "api/categories",
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Data is null)
            {
                LogApiFailure(response.StatusCode, "categories");
                return (null, "Categories could not be loaded. Please try again.");
            }

            return (response.Data, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API request for categories failed.");
            return (null, "We couldn't reach RaceDay. Please try again later.");
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API request for categories timed out.");
            return (null, "The request timed out. Please try again.");
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The API returned invalid category data.");
            return (null, "The API response could not be read. Please try again.");
        }
    }

    private async Task<OrganiserEventViewModel?> FindOwnedEventAsync(
        int eventID,
        CancellationToken cancellationToken)
    {
        var (events, _) = await LoadEventsAsync(cancellationToken);
        return events?.FirstOrDefault(item => item.EventID == eventID);
    }

    private async Task<bool> EventIsOwnedAsync(int eventID, CancellationToken cancellationToken) =>
        await FindOwnedEventAsync(eventID, cancellationToken) is not null;

    private async Task<ApiOrganiserEnrolmentResponse?> FindOwnedEnrolmentAsync(
        int eventID,
        int entryID,
        CancellationToken cancellationToken)
    {
        if (!await EventIsOwnedAsync(eventID, cancellationToken))
        {
            return null;
        }

        var response = await apiClient.GetAsync<List<ApiOrganiserEnrolmentResponse>>(
            $"api/events/{eventID}/enrolments",
            cancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? response.Data?.FirstOrDefault(item => item.EntryID == entryID)
            : null;
    }

    private async Task<bool> IsValidEntryForEventAsync(
        int eventID,
        int entryID,
        CancellationToken cancellationToken) =>
        await FindOwnedEnrolmentAsync(eventID, entryID, cancellationToken) is not null;

    private async Task<ApiOrganiserResultResponse?> FindOwnedResultAsync(
        int eventID,
        int resultID,
        CancellationToken cancellationToken)
    {
        if (!await EventIsOwnedAsync(eventID, cancellationToken))
        {
            return null;
        }

        var response = await apiClient.GetAsync<List<ApiOrganiserResultResponse>>(
            $"api/events/{eventID}/results",
            cancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? response.Data?.FirstOrDefault(item => item.ResultID == resultID)
            : null;
    }

    private async Task<string?> SendDeleteAsync(
        string requestUri,
        string resourceName,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.DeleteAuthenticatedAsync<object>(
                requestUri,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            LogApiFailure(response.StatusCode, $"delete {resourceName}");
            return response.StatusCode == HttpStatusCode.Conflict
                ? $"This {resourceName} cannot be deleted while related records exist."
                : $"The {resourceName} could not be deleted. Refresh and try again.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API delete request failed for {Resource}.", resourceName);
            return $"We couldn't reach RaceDay. The {resourceName} was not confirmed deleted.";
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API delete request timed out for {Resource}.", resourceName);
            return $"The request timed out. Check whether the {resourceName} was deleted before retrying.";
        }
    }

    private async Task<string?> SendCancelAsync(int eventID, CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.PostAuthenticatedAsync<object>(
                $"api/events/{eventID}/cancel",
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            LogApiFailure(response.StatusCode, "cancel event");
            return "The event could not be cancelled. Refresh and try again.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The API event cancellation request failed.");
            return "We couldn't reach RaceDay. The event was not confirmed cancelled.";
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "The API event cancellation request timed out.");
            return "The request timed out. Check My Events before retrying.";
        }
    }

    private IActionResult EventRequestFailure(
        Exception exception,
        OrganiserEventFormViewModel model,
        bool timedOut = false)
    {
        if (timedOut)
        {
            logger.LogWarning(exception, "The API event write request timed out.");
            ModelState.AddModelError(string.Empty, "The request timed out. Check My Events before retrying.");
        }
        else
        {
            logger.LogError(exception, "The API event write request failed.");
            ModelState.AddModelError(string.Empty, "We couldn't reach RaceDay. Your changes were not saved.");
        }

        return View(model.EventID.HasValue ? "EditEvent" : "CreateEvent", model);
    }

    private IActionResult CategoryRequestFailure(
        Exception exception,
        OrganiserCategoryFormViewModel model,
        bool timedOut = false)
    {
        if (timedOut)
        {
            logger.LogWarning(exception, "The API category write request timed out.");
            ModelState.AddModelError(string.Empty, "The request timed out. Check event details before retrying.");
        }
        else
        {
            logger.LogError(exception, "The API category write request failed.");
            ModelState.AddModelError(string.Empty, "We couldn't reach RaceDay. Your changes were not saved.");
        }

        return View(model.CategoryID.HasValue ? "EditCategory" : "CreateCategory", model);
    }

    private IActionResult ResultRequestFailure(
        Exception exception,
        OrganiserResultFormViewModel model,
        bool timedOut = false)
    {
        if (timedOut)
        {
            logger.LogWarning(exception, "The API result write request timed out.");
            ModelState.AddModelError(string.Empty, "The request timed out. Check event results before retrying.");
        }
        else
        {
            logger.LogError(exception, "The API result write request failed.");
            ModelState.AddModelError(string.Empty, "We couldn't reach RaceDay. Your changes were not saved.");
        }

        return View(model.ResultID.HasValue ? "EditResult" : "CreateResult", model);
    }

    private void AddFormApiError(HttpStatusCode statusCode, string defaultMessage)
    {
        LogApiFailure(statusCode, "organiser form submission");
        var message = statusCode switch
        {
            HttpStatusCode.Unauthorized => "Your sign-in session was rejected. Sign in again.",
            HttpStatusCode.Forbidden => "You do not have permission to manage this resource.",
            HttpStatusCode.NotFound => "The selected resource could not be found. Refresh and try again.",
            HttpStatusCode.Conflict => "The request conflicts with existing event records.",
            _ => defaultMessage
        };
        ModelState.AddModelError(string.Empty, message);
    }

    private void LogApiFailure(HttpStatusCode statusCode, string operation)
    {
        if ((int)statusCode >= 500)
        {
            logger.LogError(
                "The API returned HTTP {StatusCode} during {Operation}.",
                (int)statusCode,
                operation);
        }
        else
        {
            logger.LogWarning(
                "The API returned HTTP {StatusCode} during {Operation}.",
                (int)statusCode,
                operation);
        }
    }

    private void ValidateMediaUploadCounts(
        OrganiserEventFormViewModel model,
        IReadOnlyList<EventMediaViewModel> existingMedia)
    {
        var retainedImages = existingMedia.Count(item =>
            item.MediaType == 0 && model.RetainedMediaIds.Contains(item.EventMediaID));
        var retainedVideo = existingMedia.Any(item => item.MediaType == 1)
            && model.RetainExistingVideo;
        var newImages = model.ImageUploads?.Count(file => file is not null) ?? 0;
        var newVideo = model.VideoUpload is not null;

        if (retainedImages + newImages > mediaOptions.Value.MaximumImagesPerEvent)
        {
            ModelState.AddModelError(
                nameof(model.ImageUploads),
                $"An event can have up to {mediaOptions.Value.MaximumImagesPerEvent} images.");
        }

        if (retainedVideo && newVideo)
        {
            ModelState.AddModelError(
                nameof(model.VideoUpload),
                "Remove the existing video before selecting a replacement.");
        }
    }

    private void ValidateRetainedMedia(
        OrganiserEventFormViewModel model,
        IReadOnlyList<EventMediaViewModel> existingMedia)
    {
        var allowedIds = existingMedia
            .Where(item => !item.IsVideo)
            .Select(item => item.EventMediaID)
            .ToHashSet();
        if (model.RetainedMediaIds.Any(id => !allowedIds.Contains(id)))
        {
            ModelState.AddModelError(
                nameof(model.RetainedMediaIds),
                "The submitted media selection does not belong to this event.");
        }

        if (!existingMedia.Any(item => item.MediaType == 1))
        {
            model.RetainExistingVideo = false;
        }
    }

    private async Task StoreNewMediaAsync(
        OrganiserEventFormViewModel model,
        ICollection<StoredEventMedia> storedMedia,
        CancellationToken cancellationToken,
        int startOrder = 0)
    {
        var order = startOrder;
        foreach (var image in model.ImageUploads ?? [])
        {
            storedMedia.Add(await mediaStorage.SaveImageAsync(image, order++, cancellationToken));
        }

        if (model.VideoUpload is not null)
        {
            storedMedia.Add(await mediaStorage.SaveVideoAsync(
                model.VideoUpload,
                order,
                cancellationToken));
        }
    }

    private async Task CleanupStoredMediaAsync(
        IEnumerable<StoredEventMedia> media,
        string reason)
    {
        foreach (var item in media)
        {
            try
            {
                await mediaStorage.DeleteAsync(item.RelativePath, CancellationToken.None);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogError(
                    exception,
                    "Could not remove newly stored event media {RelativePath} after {Reason}.",
                    item.RelativePath,
                    reason);
            }
        }
    }

    private async Task CleanupUnreferencedMediaAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            var response = await apiClient.GetAsync<List<ApiEventResponse>>(
                "api/events",
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Data is null)
            {
                logger.LogError(
                    "Media cleanup was deferred because the API reference check returned HTTP {StatusCode}. Orphan candidates: {RelativePaths}",
                    (int)response.StatusCode,
                    string.Join(", ", paths));
                return;
            }

            var referencedPaths = response.Data
                .SelectMany(eventItem => eventItem.Media)
                .Select(media => media.RelativePath)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var path in paths.Distinct(StringComparer.Ordinal))
            {
                if (referencedPaths.Contains(path))
                {
                    continue;
                }

                if (!mediaStorage.IsSafeStoredPath(path))
                {
                    logger.LogError("Refusing to clean unsafe event media path {RelativePath}.", path);
                    continue;
                }

                try
                {
                    await mediaStorage.DeleteAsync(path, CancellationToken.None);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogError(
                        exception,
                        "Could not remove unreferenced event media {RelativePath}.",
                        path);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(
                exception,
                "Media cleanup was deferred because the API reference check failed. Orphan candidates: {RelativePaths}",
                string.Join(", ", paths));
        }
    }

    private void LogUncertainMediaOutcome(
        IEnumerable<StoredEventMedia> media,
        string reason)
    {
        var paths = media.Select(item => item.RelativePath).ToList();
        if (paths.Count > 0)
        {
            logger.LogWarning(
                "Preserving uploaded media after an uncertain API outcome ({Reason}). Check whether these files are referenced or orphaned: {RelativePaths}",
                reason,
                string.Join(", ", paths));
        }
    }

    private void ValidateEventDates(OrganiserEventFormViewModel model)
    {
        if (model.EventDate == default || model.RegistrationDeadline == default)
        {
            ModelState.AddModelError(string.Empty, "Enter both an event date and registration deadline.");
        }
        else if (model.RegistrationDeadline > model.EventDate)
        {
            ModelState.AddModelError(
                nameof(model.RegistrationDeadline),
                "The registration deadline cannot be after the event date.");
        }
    }

    private void ValidateResult(OrganiserResultFormViewModel model)
    {
        if (model.FinishTime < TimeSpan.Zero)
        {
            ModelState.AddModelError(nameof(model.FinishTime), "Finish time cannot be negative.");
        }
    }

    private static OrganiserResultsPageViewModel ResultsPage(
        OrganiserPageState state,
        IReadOnlyList<OrganiserEventViewModel> events,
        int? selectedEventID,
        IReadOnlyList<OrganiserEnrolmentViewModel> enrolments,
        IReadOnlyList<ParticipantResultViewModel> results,
        IReadOnlyDictionary<int, int> resultIdsByEntry,
        string? message = null) =>
        new(state, events, selectedEventID, enrolments, results, resultIdsByEntry, message);

    private static OrganiserEventViewModel ToEventViewModel(ApiOrganiserEventSummaryResponse item) =>
        new(
            item.EventID,
            item.EventName,
            item.Description,
            item.EventDate,
            item.Location,
            item.DistanceKM,
            item.EntryFee,
            item.MinimumEntryFee,
            item.MaximumEntryFee,
            item.EventType,
            item.Status,
            item.RegistrationDeadline,
            item.CreatedAt,
            item.TotalEnrolments,
            EventMediaMapping.ToViewModels(item.Media));

    private static OrganiserCategoryViewModel ToCategoryViewModel(ApiOrganiserCategoryResponse item) =>
        new(
            item.CategoryID,
            item.EventID,
            item.CategoryName,
            item.DistanceKM,
            item.MaxParticipants,
            item.EntryFee);

    private static OrganiserEventViewModel EmptyEvent(int id) =>
        new(id, string.Empty, string.Empty, default, string.Empty, 0, null, null, null, -1, 0, default, default, 0);

    private void ValidateEntryFee(decimal entryFee, string propertyName)
    {
        if (entryFee != decimal.Round(entryFee, 2))
        {
            ModelState.AddModelError(propertyName, "Enter an amount with no more than two decimal places.");
        }
    }
}
