using System.ComponentModel.DataAnnotations;

namespace RaceDay.API.DTOs;

public sealed class LoginUserRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}
