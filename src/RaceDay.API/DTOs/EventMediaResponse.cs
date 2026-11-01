using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed record EventMediaResponse(
    int EventMediaID,
    EventMediaType MediaType,
    string RelativePath,
    string ContentType,
    long SizeBytes,
    double? DurationSeconds,
    int DisplayOrder);
