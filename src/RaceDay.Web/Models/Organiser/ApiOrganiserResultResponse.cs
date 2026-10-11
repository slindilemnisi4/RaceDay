namespace RaceDay.Web.Models.Organiser;

public sealed class ApiOrganiserResultResponse
{
    public int ResultID { get; init; }

    public int EntryID { get; init; }

    public string EventName { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public TimeSpan FinishTime { get; init; }

    public int OverallPosition { get; init; }

    public int CategoryPosition { get; init; }

    public int ResultStatus { get; init; }
}

public sealed class ApiOrganiserCategoryResponse
{
    public int CategoryID { get; init; }

    public int EventID { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public decimal? DistanceKM { get; init; }

    public int? MaxParticipants { get; init; }

    public decimal EntryFee { get; init; }
}

public sealed class ApiOrganiserProfileResponse
{
    public int Role { get; init; }

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
}
