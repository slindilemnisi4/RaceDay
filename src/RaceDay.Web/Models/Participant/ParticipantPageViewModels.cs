using System.ComponentModel.DataAnnotations;
using RaceDay.Web.Models.Events;

namespace RaceDay.Web.Models.Participant;

public enum ParticipantPageState
{
    Success,
    Empty,
    Failure
}

public sealed record MyRacesViewModel(
    ParticipantPageState State,
    IReadOnlyList<ParticipantRaceViewModel> Races,
    string? Message = null);

public sealed record ParticipantRaceViewModel(
    string EventName,
    DateOnly? EventDate,
    string? Location,
    string CategoryName,
    decimal? EntryFee,
    DateTime EntryDate);

public sealed record MyResultsViewModel(
    ParticipantPageState State,
    IReadOnlyList<ParticipantResultViewModel> Results,
    string? Message = null);

public sealed record ParticipantResultViewModel(
    string EventName,
    string CategoryName,
    TimeSpan FinishTime,
    int OverallPosition,
    int CategoryPosition,
    int ResultStatus)
{
    public string ResultStatusLabel => ResultStatus switch
    {
        0 => "Finished",
        1 => "Did not finish",
        2 => "Did not start",
        3 => "Disqualified",
        _ => "Unknown"
    };
}

public sealed class ParticipantProfileViewModel
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

    public string Role { get; set; } = "Participant";

    [Display(Name = "Account created")]
    public DateTime CreatedAt { get; set; }
}

public enum JoinRacePageState
{
    Success,
    Empty,
    Failure
}

public sealed record JoinRaceViewModel(
    JoinRacePageState State,
    IReadOnlyList<JoinRaceEventViewModel> Events,
    int? SelectedEventID = null,
    string? Message = null);

public sealed record JoinRaceEventViewModel(
    int EventID,
    string EventName,
    DateOnly EventDate,
    string Location,
    IReadOnlyList<EventCategoryViewModel> Categories);
