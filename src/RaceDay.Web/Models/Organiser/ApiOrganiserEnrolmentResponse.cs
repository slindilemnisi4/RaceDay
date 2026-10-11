namespace RaceDay.Web.Models.Organiser;

public sealed class ApiOrganiserEnrolmentResponse
{
    public int EntryID { get; init; }

    public int EventID { get; init; }

    public string EventName { get; init; } = string.Empty;

    public int CategoryID { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public DateTime EntryDate { get; init; }
}
