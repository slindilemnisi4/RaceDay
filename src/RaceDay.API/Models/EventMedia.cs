namespace RaceDay.API.Models;

public enum EventMediaType
{
    Image,
    Video
}

public sealed class EventMedia
{
    public int EventMediaID { get; set; }

    public int EventID { get; set; }

    public EventMediaType MediaType { get; set; }

    public string RelativePath { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public double? DurationSeconds { get; set; }

    public int DisplayOrder { get; set; }

    public Event Event { get; set; } = null!;
}
