using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed class EventResponse
{
    public int EventID { get; init; }

    public int OrganizerID { get; init; }

    public string EventName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateOnly EventDate { get; init; }

    public string Location { get; init; } = string.Empty;

    public decimal DistanceKM { get; init; }

    public EventTypeOption EventType { get; init; }

    public DateOnly RegistrationDeadline { get; init; }

    public DateTime CreatedAt { get; init; }
}
