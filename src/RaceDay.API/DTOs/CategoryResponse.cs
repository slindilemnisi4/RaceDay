namespace RaceDay.API.DTOs;

public sealed class CategoryResponse
{
    public int CategoryID { get; init; }

    public int EventID { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public decimal? DistanceKM { get; init; }

    public int? MaxParticipants { get; init; }

    public decimal EntryFee { get; init; }
}
