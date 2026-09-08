using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RaceDay.API.Models;

namespace RaceDay.API.Services;

/// <summary>
/// Creates signed JWTs for users whose credentials have already been verified.
/// </summary>
public sealed class JwtTokenService
{
    private readonly IConfiguration configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    /// <summary>
    /// Generates a token containing the user's identity and role for later
    /// authorization. Passwords and password hashes are never included.
    /// </summary>
    public string GenerateToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var jwtConfiguration = configuration.GetSection("Jwt");
        var secretKey = jwtConfiguration["SecretKey"];
        var issuer = jwtConfiguration["Issuer"];
        var audience = jwtConfiguration["Audience"];
        var expirationMinutes = jwtConfiguration.GetValue<double>("ExpirationMinutes");

        if (string.IsNullOrWhiteSpace(secretKey)
            || string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(audience)
            || expirationMinutes <= 0)
        {
            throw new InvalidOperationException("JWT configuration is incomplete or invalid.");
        }

        // Signing prevents clients from changing identity claims without the
        // configured secret key. The bearer middleware validates this signature.
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("UserID", user.UserID.ToString()),
            new Claim("Email", user.Email),
            new Claim("Role", user.Role.ToString())
        };

        // Expiration is configuration-driven so deployments can choose an
        // appropriate token lifetime without changing source code.
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
