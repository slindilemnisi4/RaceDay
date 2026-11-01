namespace RaceDay.Web.Models.Events;

public enum EventDiscoveryState
{
    PublicLanding,
    Success,
    Empty,
    Failure
}

public sealed record EventDiscoveryViewModel(
    EventDiscoveryState State,
    IReadOnlyList<EventCardViewModel> Events,
    string? Message = null)
{
    public static EventDiscoveryViewModel PublicLanding { get; } =
        new(EventDiscoveryState.PublicLanding, []);

    public static EventDiscoveryViewModel Empty { get; } =
        new(EventDiscoveryState.Empty, []);

    public static EventDiscoveryViewModel Failure { get; } =
        new(EventDiscoveryState.Failure, []);

    public static EventDiscoveryViewModel Forbidden { get; } =
        new(
            EventDiscoveryState.Failure,
            [],
            "Your account does not have permission to view events.");

    public static EventDiscoveryViewModel Success(
        IReadOnlyList<EventCardViewModel> events) =>
        new(EventDiscoveryState.Success, events);
}

public sealed record EventCardViewModel(
    int EventID,
    string EventName,
    string Description,
    DateOnly EventDate,
    string Location,
    decimal DistanceKM,
    int EventType,
    DateOnly RegistrationDeadline,
    decimal? EntryFee,
    int Status,
    decimal? MinimumEntryFee,
    decimal? MaximumEntryFee,
    IReadOnlyList<EventCategoryViewModel>? Categories = null,
    IReadOnlyList<EventMediaViewModel>? Media = null)
{
    public IReadOnlyList<EventCategoryViewModel> AvailableCategories { get; } =
        Categories ?? [];

    public IReadOnlyList<EventMediaViewModel> EventMedia { get; } = Media ?? [];

    public string EventTypeLabel => EventType switch
    {
        0 => "Run",
        1 => "Walk",
        2 => "Cycle",
        _ => "Unknown type"
    };

    public string EntryFeeLabel =>
        EntryFeeFormatter.FormatRange(MinimumEntryFee, MaximumEntryFee, EntryFee);

    public bool IsCancelled => Status == 1;

    public EventJoinActionViewModel JoinAction =>
        new(EventID, IsCancelled, AvailableCategories);

    public EventMediaGalleryViewModel MediaGallery => new(EventID, EventMedia);
}

public sealed record EventCategoryViewModel(
    int CategoryID,
    string CategoryName,
    decimal? DistanceKM,
    decimal EntryFee)
{
    public string EntryFeeLabel => EntryFeeFormatter.Format(EntryFee);
}

public sealed record EventJoinActionViewModel(
    int EventID,
    bool IsCancelled,
    IReadOnlyList<EventCategoryViewModel> Categories);
