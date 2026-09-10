using System.ComponentModel.DataAnnotations;

namespace RaceDay.API.DTOs;

public sealed class UpdateUserProfileRequest
{
    [Required]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    public string LastName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;
}
