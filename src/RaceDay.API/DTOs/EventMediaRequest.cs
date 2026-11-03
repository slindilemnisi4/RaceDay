using System.ComponentModel.DataAnnotations;
using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed record EventMediaRequest(
    EventMediaType MediaType,
    [param: Required] string RelativePath,
    [param: Required] string ContentType,
    long SizeBytes,
    double? DurationSeconds,
    int DisplayOrder);
