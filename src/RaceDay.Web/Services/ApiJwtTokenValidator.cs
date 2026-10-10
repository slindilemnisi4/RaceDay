using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RaceDay.Web.Services;

public sealed class ApiJwtTokenValidator(IConfiguration apiJwtConfiguration)
{
    public ValidatedApiIdentity Validate(LoginApiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var secretKey = apiJwtConfiguration["Jwt:SecretKey"];
        var issuer = apiJwtConfiguration["Jwt:Issuer"];
        var audience = apiJwtConfiguration["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(secretKey)
            || string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("The API JWT validation configuration is incomplete.");
        }

        if (string.IsNullOrWhiteSpace(response.Token)
            || response.UserID <= 0
            || string.IsNullOrWhiteSpace(response.FirstName)
            || string.IsNullOrWhiteSpace(response.LastName)
            || string.IsNullOrWhiteSpace(response.Email)
            || response.Role is not 0 and not 1)
        {
            throw new SecurityTokenException("The API login response is incomplete.");
        }

        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        var principal = handler.ValidateToken(
            response.Token,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.Zero,
                RoleClaimType = "Role",
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            },
            out var validatedToken);

        if (validatedToken is not JwtSecurityToken jwt
            || jwt.ValidTo <= DateTime.UtcNow
            || !int.TryParse(
                principal.FindFirst("UserID")?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var tokenUserId)
            || tokenUserId != response.UserID
            || !string.Equals(
                principal.FindFirst("Email")?.Value,
                response.Email,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                principal.FindFirst("Role")?.Value,
                response.Role == 0 ? "Organiser" : "Participant",
                StringComparison.Ordinal))
        {
            throw new SecurityTokenException("The API login identity does not match its validated token.");
        }

        return new ValidatedApiIdentity(
            tokenUserId,
            response.FirstName.Trim(),
            response.LastName.Trim(),
            response.Email.Trim(),
            response.Role == 0 ? "Organiser" : "Participant",
            new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero));
    }

    public ValidatedApiIdentity ValidateProfile(
        ValidatedApiIdentity identity,
        UserProfileApiResponse profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.UserID != identity.UserId
            || profile.Role is not 0 and not 1
            || !string.Equals(
                profile.Role == 0 ? "Organiser" : "Participant",
                identity.Role,
                StringComparison.Ordinal)
            || !string.Equals(profile.Email, identity.Email, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(profile.FirstName)
            || string.IsNullOrWhiteSpace(profile.LastName))
        {
            throw new SecurityTokenException("The API profile does not match the validated token.");
        }

        return identity with
        {
            FirstName = profile.FirstName.Trim(),
            LastName = profile.LastName.Trim(),
            Email = profile.Email.Trim()
        };
    }
}

public sealed record ValidatedApiIdentity(
    int UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    DateTimeOffset ExpiresAt);

public sealed record LoginApiResponse(
    int UserID,
    string FirstName,
    string LastName,
    string Email,
    int Role,
    string Token);

public sealed record UserProfileApiResponse(
    int UserID,
    string FirstName,
    string LastName,
    string Email,
    int Role,
    DateTime CreatedAt);
