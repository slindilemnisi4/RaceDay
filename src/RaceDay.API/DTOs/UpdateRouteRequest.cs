using System.ComponentModel.DataAnnotations;

namespace RaceDay.API.DTOs;

public sealed class UpdateRouteRequest
{
    [Required]
    public string RouteName { get; init; } = string.Empty;

    [Range(0.01, double.MaxValue)]
    public decimal DistanceKM { get; init; }

    [Range(0, double.MaxValue)]
    public decimal? ElevationGainM { get; init; }

    public string? RouteDescription { get; init; }

    public string? MapURL { get; init; }
}