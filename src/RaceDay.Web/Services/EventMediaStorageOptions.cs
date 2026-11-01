namespace RaceDay.Web.Services;

public sealed class EventMediaStorageOptions
{
    public const string SectionName = "EventMedia";

    public string StorageDirectory { get; set; } = "wwwroot/EventMedia";

    public string PublicPath { get; set; } = "/EventMedia";

    public long MaximumImageBytes { get; set; } = 5 * 1024 * 1024;

    public long MaximumVideoBytes { get; set; } = 20 * 1024 * 1024;

    public int MaximumImagesPerEvent { get; set; } = 5;

    public double MaximumVideoDurationSeconds { get; set; } = 30;

    public string FfprobePath { get; set; } = "ffprobe";
}

public sealed class EventMediaValidationException(string message) : Exception(message);

public sealed record StoredEventMedia(
    int MediaType,
    string RelativePath,
    string ContentType,
    long SizeBytes,
    double? DurationSeconds,
    int DisplayOrder);
