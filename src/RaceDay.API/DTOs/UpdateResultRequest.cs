using System.ComponentModel.DataAnnotations;
using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed class UpdateResultRequest
{
    public TimeSpan FinishTime { get; init; }

    [Range(1, int.MaxValue)]
    public int OverallPosition { get; init; }

    [Range(1, int.MaxValue)]
    public int CategoryPosition { get; init; }

    [Required]
    public ResultStatus? ResultStatus { get; init; }
}
