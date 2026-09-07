using System.ComponentModel.DataAnnotations;
using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed class RegisterUserRequest
{
    [Required]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    public string LastName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    public UserRole? Role { get; init; }
}
