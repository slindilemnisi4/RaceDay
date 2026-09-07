using Microsoft.AspNetCore.Identity;
using RaceDay.API.Models;

namespace RaceDay.API.Services;

/// <summary>
/// Hashes and verifies user passwords without retaining the original password.
/// </summary>
public sealed class PasswordHashService
{
    private readonly PasswordHasher<User> passwordHasher = new();

    /// <summary>
    /// Creates a one-way password hash suitable for storing in User.PasswordHash.
    /// The original password must never be stored because a database compromise
    /// must not expose users' credentials directly.
    /// </summary>
    public string HashPassword(User user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return passwordHasher.HashPassword(user, password);
    }

    /// <summary>
    /// Verifies a supplied password by comparing it with the hash's embedded salt
    /// and work-factor settings. The original password is not recovered or returned.
    /// </summary>
    public bool VerifyPassword(User user, string password, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var verificationResult = passwordHasher.VerifyHashedPassword(user, passwordHash, password);

        return verificationResult is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
