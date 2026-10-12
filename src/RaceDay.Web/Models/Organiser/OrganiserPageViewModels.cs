using System.ComponentModel.DataAnnotations;
using System.Globalization;
using RaceDay.Web.Models.Events;
using RaceDay.Web.Models.Participant;

namespace RaceDay.Web.Models.Organiser;

public enum OrganiserPageState
{
    Success,
    Empty,
    Failure
}

public sealed record OrganiserDashboardViewModel(
    OrganiserPageState State,
    IReadOnlyList<OrganiserEventViewModel> Events,
    OrganiserPageState PlatformEventsState,
    IReadOnlyList<EventCardViewModel> PlatformEvents,
    string? Message = null,
    string? PlatformEventsMessage = null)
{
    public int TotalEvents => Events.Count;

    public int UpcomingEvents => Events.Count(item => item.EventDate >= DateOnly.FromDateTime(DateTime.Today));

    public int PastEvents => Events.Count(item => item.EventDate < DateOnly.FromDateTime(DateTime.Today));

    public int TotalEnrolments => Events.Sum(item => item.TotalEnrolments);
}

public sealed record OrganiserEventViewModel(
    int EventID,
    string EventName,
    string Description,
    DateOnly EventDate,
    string Location,
    decimal DistanceKM,
    decimal? EntryFee,
    int EventType,
    int Status,
    DateOnly RegistrationDeadline,
    DateTime CreatedAt,
    int TotalEnrolments)
{
    public string EventTypeLabel => EventType switch
    {
        0 => "Run",
        1 => "Walk",
        2 => "Cycle",
        _ => "Unknown type"
    };

    public bool IsCancelled => Status == 1;

    public string EntryFeeLabel => EntryFee is decimal fee
        ? $"R{fee.ToString("0.00", CultureInfo.GetCultureInfo("en-ZA"))}"
        : "See category fees";
}

public sealed record OrganiserEventsPageViewModel(
    OrganiserPageState State,
    IReadOnlyList<OrganiserEventViewModel> Events,
    string? Message = null);

public sealed record OrganiserEventDetailsViewModel(
    OrganiserEventViewModel Event,
    IReadOnlyList<OrganiserCategoryViewModel> Categories,
    string? Message = null);

public sealed record OrganiserCategoryViewModel(
    int CategoryID,
    int EventID,
    string CategoryName,
    decimal? DistanceKM,
    int? MaxParticipants,
    decimal EntryFee);

public sealed class OrganiserEventFormViewModel
{
    public int? EventID { get; set; }

    [Required]
    [StringLength(150)]
    [Display(Name = "Event name")]
    public string EventName { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Event date")]
    public DateOnly EventDate { get; set; }

    [Required]
    [StringLength(150)]
    public string Location { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999.99")]
    [Display(Name = "Distance (km)")]
    public decimal DistanceKM { get; set; }

    [Range(typeof(decimal), "0", "99999999.99")]
    [Display(Name = "Entry fee (R)")]
    public decimal EntryFee { get; set; }

    [Range(0, 2)]
    [Display(Name = "Event type")]
    public int EventType { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Registration deadline")]
    public DateOnly RegistrationDeadline { get; set; }
}

public sealed class OrganiserCategoryFormViewModel
{
    public int? CategoryID { get; set; }

    public int EventID { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Category name")]
    public string CategoryName { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999.99")]
    [Display(Name = "Distance (km)")]
    public decimal? DistanceKM { get; set; }

    [Range(1, int.MaxValue)]
    [Display(Name = "Maximum participants")]
    public int? MaxParticipants { get; set; }

    [Range(typeof(decimal), "0", "99999999.99")]
    [Display(Name = "Entry fee (R)")]
    public decimal EntryFee { get; set; }
}

public sealed record OrganiserEnrolmentViewModel(
    int EntryID,
    int CategoryID,
    string CategoryName,
    DateTime EntryDate);

public sealed record OrganiserResultsPageViewModel(
    OrganiserPageState State,
    IReadOnlyList<OrganiserEventViewModel> Events,
    int? SelectedEventID,
    IReadOnlyList<OrganiserEnrolmentViewModel> Enrolments,
    IReadOnlyList<ParticipantResultViewModel> Results,
    IReadOnlyDictionary<int, int> ResultIdsByEntry,
    string? Message = null);

public sealed class OrganiserResultFormViewModel
{
    public int EventID { get; set; }

    public int EntryID { get; set; }

    public int? ResultID { get; set; }

    [Required]
    [DataType(DataType.Time)]
    [Display(Name = "Finish time")]
    public TimeSpan FinishTime { get; set; }

    [Range(1, int.MaxValue)]
    [Display(Name = "Overall position")]
    public int OverallPosition { get; set; }

    [Range(1, int.MaxValue)]
    [Display(Name = "Category position")]
    public int CategoryPosition { get; set; }

    [Range(0, 3)]
    [Display(Name = "Result status")]
    public int ResultStatus { get; set; }
}

public sealed class OrganiserProfileViewModel
{
    [Required]
    [StringLength(50)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = "Organiser";

    [Display(Name = "Account created")]
    public DateTime CreatedAt { get; set; }
}
