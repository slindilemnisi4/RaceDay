using System.ComponentModel.DataAnnotations;

namespace RaceDay.API.DTOs;

public sealed class CreateEnrolmentRequest
{
    [Range(1, int.MaxValue)]
    public int CategoryID { get; init; }
}
