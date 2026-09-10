using System.ComponentModel.DataAnnotations;
using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed class CreateEventRequest
{
    [Required]
    public string EventName { get; init; } = string.Empty;

    [Required]
    public string Description { get; init; } = string.Empty;

    public DateOnly EventDate { get; init; }

    [Required]
    public string Location { get; init; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999.99")]
    public decimal DistanceKM { get; init; }

    [Required]
    public EventTypeOption? EventType { get; init; }

    public DateOnly RegistrationDeadline { get; init; }
}
