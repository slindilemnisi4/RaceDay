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

    public static EventDiscoveryViewModel Success(IReadOnlyList<EventCardViewModel> events) =>
        new(EventDiscoveryState.Success, events);
}

public sealed record EventCardViewModel(
    string EventName,
    string Description,
    DateOnly EventDate,
    string Location,
    decimal DistanceKM,
    int EventType,
    DateOnly RegistrationDeadline)
{
    public string EventTypeLabel => EventType switch
    {
        0 => "Run",
        1 => "Walk",
        2 => "Cycle",
        _ => "Unknown type"
    };
}
