using RaceDay.API.Models;

namespace RaceDay.API.DTOs;

public sealed class UserProfileResponse
{
    public int UserID { get; init; }

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public UserRole Role { get; init; }

    public DateTime CreatedAt { get; init; }
}
