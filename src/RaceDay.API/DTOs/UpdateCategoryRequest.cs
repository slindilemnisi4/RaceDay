using System.ComponentModel.DataAnnotations;

namespace RaceDay.API.DTOs;

public sealed class UpdateCategoryRequest
{
    [Required]
    public string CategoryName { get; init; } = string.Empty;

    public decimal? DistanceKM { get; init; }

    public int? MaxParticipants { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal EntryFee { get; init; }
}
