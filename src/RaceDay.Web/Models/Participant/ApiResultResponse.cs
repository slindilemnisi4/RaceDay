namespace RaceDay.Web.Models.Participant;

public sealed class ApiResultResponse
{
    public string EventName { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public TimeSpan FinishTime { get; init; }

    public int OverallPosition { get; init; }

    public int CategoryPosition { get; init; }

    public int ResultStatus { get; init; }
}
