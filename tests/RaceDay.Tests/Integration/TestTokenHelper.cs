using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RaceDay.Tests.Integration;

/// <summary>
/// Creates JWT tokens for integration tests so requests can pass through
/// the application's real authentication and authorization middleware.
/// </summary>
public static class TestTokenHelper
{
    private const string SecretKey = "DEV_ONLY_REPLACE_WITH_A_SECURE_SECRET_KEY";
    private const string Issuer = "RaceDay.API";
    private const string Audience = "RaceDay.Client";

    public static string CreateToken(int userId, string email, string role)
    {
        var claims = new[]
        {
            new Claim("UserID", userId.ToString()),
            new Claim("Email", email),
            new Claim("Role", role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}