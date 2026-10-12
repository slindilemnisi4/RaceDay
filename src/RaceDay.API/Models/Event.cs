namespace RaceDay.API.Models;

/// <summary>
/// Represents a RaceDay event created by an organiser.
/// </summary>
public enum EventTypeOption
{
    Run,
    Walk,
    Cycle
}

public enum EventStatus
{
    Active,
    Cancelled
}

public class Event
{
    public int EventID { get; set; }

    public int OrganizerID { get; set; }

    public string EventName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateOnly EventDate { get; set; }

    public string Location { get; set; } = string.Empty;

    public decimal DistanceKM { get; set; }

    public EventTypeOption EventType { get; set; }

    public DateOnly RegistrationDeadline { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Active;

    public DateTime CreatedAt { get; set; }

    // Organiser/User relationship: many events can belong to one organiser.
    public User Organizer { get; set; } = null!;

    // An event can contain many categories.
    public ICollection<Category>? Categories { get; set; } = new List<Category>();

    // A route is associated with the event and is unique per event in the ERD.
    public Route? Route { get; set; }
}
