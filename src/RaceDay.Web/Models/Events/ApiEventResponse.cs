namespace RaceDay.Web.Models.Events;

public sealed class ApiEventResponse
{
    public string EventName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateOnly EventDate { get; init; }

    public string Location { get; init; } = string.Empty;

    public decimal DistanceKM { get; init; }

    // The API uses System.Text.Json's default numeric enum representation.
    public int EventType { get; init; }

    public DateOnly RegistrationDeadline { get; init; }
}
