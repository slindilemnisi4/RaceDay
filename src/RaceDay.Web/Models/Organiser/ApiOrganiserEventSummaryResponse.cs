namespace RaceDay.Web.Models.Organiser;

public sealed class ApiOrganiserEventSummaryResponse
{
    public int EventID { get; init; }

    public string EventName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateOnly EventDate { get; init; }

    public string Location { get; init; } = string.Empty;

    public decimal DistanceKM { get; init; }

    public decimal? EntryFee { get; init; }

    public int EventType { get; init; }

    public int Status { get; init; }

    public DateOnly RegistrationDeadline { get; init; }

    public DateTime CreatedAt { get; init; }

    public int TotalEnrolments { get; init; }
}
