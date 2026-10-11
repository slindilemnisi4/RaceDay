using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed record OrganiserEventSummaryResponse(
    int EventID,
    string EventName,
    string Description,
    DateOnly EventDate,
    string Location,
    decimal DistanceKM,
    EventTypeOption EventType,
    DateOnly RegistrationDeadline,
    DateTime CreatedAt,
    int TotalEnrolments);
