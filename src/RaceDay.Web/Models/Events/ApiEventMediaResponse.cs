namespace RaceDay.Web.Models.Events;

public sealed class ApiEventMediaResponse
{
    public int EventMediaID { get; init; }

    public int MediaType { get; init; }

    public string RelativePath { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public double? DurationSeconds { get; init; }

    public int DisplayOrder { get; init; }
}

public sealed record EventMediaViewModel(
    int EventMediaID,
    int MediaType,
    string RelativePath,
    string ContentType,
    long SizeBytes,
    double? DurationSeconds,
    int DisplayOrder)
{
    public bool IsVideo => MediaType == 1;
}

public sealed record EventMediaGalleryViewModel(
    int EventID,
    IReadOnlyList<EventMediaViewModel> Items);

public static class EventMediaMapping
{
    public static IReadOnlyList<EventMediaViewModel> ToViewModels(
        IEnumerable<ApiEventMediaResponse>? media) =>
        (media ?? [])
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new EventMediaViewModel(
                item.EventMediaID,
                item.MediaType,
                item.RelativePath,
                item.ContentType,
                item.SizeBytes,
                item.DurationSeconds,
                item.DisplayOrder))
            .ToList();
}
