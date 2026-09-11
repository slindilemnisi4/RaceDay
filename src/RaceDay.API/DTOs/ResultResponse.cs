using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed class ResultResponse
{
    public int ResultID { get; init; }

    public int EntryID { get; init; }

    public int ParticipantUserID { get; init; }

    public int EventID { get; init; }

    public string EventName { get; init; } = string.Empty;

    public int CategoryID { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public TimeSpan FinishTime { get; init; }

    public int OverallPosition { get; init; }

    public int CategoryPosition { get; init; }

    public ResultStatus ResultStatus { get; init; }

    public DateTime RecordedAt { get; init; }
}
