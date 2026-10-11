using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RaceDay.API.Controllers;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using RaceDay.API.Services;
using Xunit;

namespace RaceDay.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Register_WithValidDetails_CreatesUserAndReturnsSafeResponse()
    {
        await using var fixture = TestFixture.Create();

        var result = await fixture.Controller.Register(
            fixture.RegisterRequest(),
            CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var response = Assert.IsType<RegisteredUserResponse>(created.Value);
        var storedUser = await fixture.DbContext.Users.SingleAsync();

        Assert.Equal("USER@EXAMPLE.COM", response.Email);
        Assert.Equal(UserRole.Participant, response.Role);
        Assert.NotEqual("CorrectHorseBatteryStaple!", storedUser.PasswordHash);
        Assert.DoesNotContain("PasswordHash", created.Value!.GetType().GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("Password", created.Value.GetType().GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        await using var fixture = TestFixture.Create();
        await fixture.Controller.Register(fixture.RegisterRequest(), CancellationToken.None);

        var result = await fixture.Controller.Register(
            fixture.RegisterRequest(email: "user@example.com"),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task Register_OrganiserWithoutRegistrationCode_IsForbidden()
    {
        await using var fixture = TestFixture.Create();

        var result = await fixture.Controller.Register(
            fixture.RegisterRequest(role: UserRole.Organiser),
            CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Empty(await fixture.DbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task Register_OrganiserWithInvalidRegistrationCode_IsForbidden()
    {
        await using var fixture = TestFixture.Create();

        var result = await fixture.Controller.Register(
            fixture.RegisterRequest(role: UserRole.Organiser, organiserCode: "incorrect-test-code"),
            CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Empty(await fixture.DbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task Register_OrganiserWithConfiguredRegistrationCode_CreatesOrganiser()
    {
        await using var fixture = TestFixture.Create();

        var result = await fixture.Controller.Register(
            fixture.RegisterRequest(role: UserRole.Organiser, organiserCode: TestFixture.OrganiserCode),
            CancellationToken.None);

        Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(UserRole.Organiser, (await fixture.DbContext.Users.SingleAsync()).Role);
    }

    [Fact]
    public async Task Register_OrganiserWithAnotherConfiguredRegistrationCode_CreatesOrganiser()
    {
        await using var fixture = TestFixture.Create();

        var result = await fixture.Controller.Register(
            fixture.RegisterRequest(role: UserRole.Organiser, organiserCode: "2059"),
            CancellationToken.None);

        Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(UserRole.Organiser, (await fixture.DbContext.Users.SingleAsync()).Role);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    public async Task Register_OrganiserWithMalformedInvitationCode_IsForbidden(string code)
    {
        await using var fixture = TestFixture.Create();

        var result = await fixture.Controller.Register(
            fixture.RegisterRequest(role: UserRole.Organiser, organiserCode: code),
            CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Empty(await fixture.DbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkAndJwtToken()
    {
        await using var fixture = TestFixture.Create();
        await fixture.Controller.Register(fixture.RegisterRequest(), CancellationToken.None);

        var result = await fixture.Controller.Login(
            new LoginUserRequest
            {
                Email = "user@example.com",
                Password = "CorrectHorseBatteryStaple!"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<LoginUserResponse>(ok.Value);
        var token = fixture.ReadToken(response.Token);

        Assert.Equal("USER@EXAMPLE.COM", response.Email);
        Assert.NotNull(response.Token);
        Assert.Equal("1", token.Claims.Single(claim => claim.Type == "UserID").Value);
        Assert.Equal("USER@EXAMPLE.COM", token.Claims.Single(claim => claim.Type == "Email").Value);
        Assert.Equal("Participant", token.Claims.Single(claim => claim.Type == "Role").Value);
        Assert.DoesNotContain(token.Claims, claim => claim.Type.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsSameUnauthorizedResponseAsUnknownEmail()
    {
        await using var fixture = TestFixture.Create();
        await fixture.Controller.Register(fixture.RegisterRequest(), CancellationToken.None);

        var invalidPassword = await fixture.Controller.Login(
            new LoginUserRequest { Email = "user@example.com", Password = "wrong" },
            CancellationToken.None);
        var unknownEmail = await fixture.Controller.Login(
            new LoginUserRequest { Email = "missing@example.com", Password = "wrong" },
            CancellationToken.None);

        var invalidPasswordResult = Assert.IsType<UnauthorizedObjectResult>(invalidPassword.Result);
        var unknownEmailResult = Assert.IsType<UnauthorizedObjectResult>(unknownEmail.Result);
        Assert.Equal(invalidPasswordResult.StatusCode, unknownEmailResult.StatusCode);
        Assert.Equal(invalidPasswordResult.Value!.ToString(), unknownEmailResult.Value!.ToString());
    }

    [Fact]
    public async Task JwtTokenService_UsesConfiguredIssuerAudienceSigningKeyAndLifetime()
    {
        await using var fixture = TestFixture.Create();
        var user = new User
        {
            UserID = 7,
            Email = "USER@EXAMPLE.COM",
            Role = UserRole.Organiser
        };

        var token = fixture.TokenService.GenerateToken(user);
        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestFixture.SecretKey)),
            ValidateIssuer = true,
            ValidIssuer = TestFixture.Issuer,
            ValidateAudience = true,
            ValidAudience = TestFixture.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "Role"
        };

        var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

        Assert.Equal("USER@EXAMPLE.COM", principal.FindFirst("Email")?.Value);
        Assert.Equal("Organiser", principal.FindFirst("Role")?.Value);
        Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.True(((JwtSecurityToken)validatedToken).ValidTo > DateTime.UtcNow);
    }

    [Fact]
    public void AuthEndpoints_AreExplicitlyAnonymous()
    {
        var methods = typeof(AuthController).GetMethods();

        Assert.NotNull(methods.Single(method => method.Name == "Register")
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(methods.Single(method => method.Name == "Login")
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).SingleOrDefault());
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        public const string SecretKey = "test-only-secret-key-that-is-long-enough-123456789";
        public const string Issuer = "RaceDay.Tests";
        public const string Audience = "RaceDay.Tests.Client";
        public const string OrganiserCode = "7316";

        private TestFixture(
            RaceDayDbContext dbContext,
            AuthController controller,
            JwtTokenService tokenService)
        {
            DbContext = dbContext;
            Controller = controller;
            TokenService = tokenService;
        }

        public RaceDayDbContext DbContext { get; }

        public AuthController Controller { get; }

        public JwtTokenService TokenService { get; }

        public static TestFixture Create()
        {
            var options = new DbContextOptionsBuilder<RaceDayDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new RaceDayDbContext(options);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SecretKey"] = SecretKey,
                    ["Jwt:Issuer"] = Issuer,
                    ["Jwt:Audience"] = Audience,
                    ["Jwt:ExpirationMinutes"] = "60",
                    ["Registration:OrganiserCodes:0"] = OrganiserCode,
                    ["Registration:OrganiserCodes:1"] = "2059"
                })
                .Build();
            var tokenService = new JwtTokenService(configuration);
            var passwordService = new PasswordHashService();
            var controller = new AuthController(dbContext, passwordService, tokenService, configuration);

            return new TestFixture(dbContext, controller, tokenService);
        }

        public RegisterUserRequest RegisterRequest(
            string email = "USER@EXAMPLE.COM",
            UserRole role = UserRole.Participant,
            string? organiserCode = null) => new()
        {
            FirstName = "Test",
            LastName = "User",
            Email = email,
            Password = "CorrectHorseBatteryStaple!",
            Role = role,
            OrganiserRegistrationCode = organiserCode
        };

        public JwtSecurityToken ReadToken(string token) =>
            new JwtSecurityTokenHandler().ReadJwtToken(token);

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
