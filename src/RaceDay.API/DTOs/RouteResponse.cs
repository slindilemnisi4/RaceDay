namespace RaceDay.API.DTOs;

public sealed class RouteResponse
{
    public int RouteID { get; init; }

    public int EventID { get; init; }

    public string RouteName { get; init; } = string.Empty;

    public decimal DistanceKM { get; init; }

    public decimal? ElevationGainM { get; init; }

    public string? RouteDescription { get; init; }

    public string? MapURL { get; init; }
}