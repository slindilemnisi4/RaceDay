using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Controllers;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using Xunit;

namespace RaceDay.Tests;

public sealed class UserProfileTests
{
    [Fact]
    public async Task GetCurrentProfile_ReturnsAuthenticatedUsersProfileWithoutPasswordHash()
    {
        await using var fixture = TestFixture.Create(UserRole.Organiser);

        var result = await fixture.Controller.GetCurrentProfile(CancellationToken.None);

        var response = Assert.IsType<UserProfileResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(fixture.User.UserID, response.UserID);
        Assert.Equal(fixture.User.Email, response.Email);
        Assert.DoesNotContain("PasswordHash", typeof(UserProfileResponse).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task GetCurrentProfile_WithoutAuthentication_ReturnsUnauthorized()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant, authenticated: false);

        var result = await fixture.Controller.GetCurrentProfile(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task UpdateCurrentProfile_PersistsEditableFieldsAndPreservesProtectedFields()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var originalRole = fixture.User.Role;
        var originalPasswordHash = fixture.User.PasswordHash;
        var originalCreatedAt = fixture.User.CreatedAt;

        var result = await fixture.Controller.UpdateCurrentProfile(
            new UpdateUserProfileRequest
            {
                FirstName = "Updated",
                LastName = "Participant",
                Email = "updated@example.com"
            },
            CancellationToken.None);

        var response = Assert.IsType<UserProfileResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var persistedUser = await fixture.DbContext.Users.SingleAsync();
        Assert.Equal("Updated", persistedUser.FirstName);
        Assert.Equal("PARTICIPANT", persistedUser.LastName.ToUpperInvariant());
        Assert.Equal("UPDATED@EXAMPLE.COM", persistedUser.Email);
        Assert.Equal(originalRole, persistedUser.Role);
        Assert.Equal(originalPasswordHash, persistedUser.PasswordHash);
        Assert.Equal(originalCreatedAt, persistedUser.CreatedAt);
        Assert.Equal(persistedUser.UserID, response.UserID);
        Assert.DoesNotContain("PasswordHash", typeof(UserProfileResponse).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task UpdateCurrentProfile_UsesJwtUserIDAndCannotTargetAnotherUser()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        var otherUser = new User
        {
            FirstName = "Other",
            LastName = "User",
            Email = "OTHER@EXAMPLE.COM",
            PasswordHash = "other-hash",
            Role = UserRole.Organiser,
            CreatedAt = DateTime.UtcNow
        };
        fixture.DbContext.Users.Add(otherUser);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.Controller.UpdateCurrentProfile(
            new UpdateUserProfileRequest
            {
                FirstName = "Mine",
                LastName = "Updated",
                Email = "mine@example.com"
            },
            CancellationToken.None);

        var persistedOtherUser = await fixture.DbContext.Users
            .AsNoTracking()
            .SingleAsync(user => user.UserID == otherUser.UserID);
        Assert.Equal("Other", persistedOtherUser.FirstName);
        Assert.Equal("OTHER@EXAMPLE.COM", persistedOtherUser.Email);
    }

    [Fact]
    public async Task UpdateCurrentProfile_WithDuplicateEmail_ReturnsConflict()
    {
        await using var fixture = TestFixture.Create(UserRole.Participant);
        fixture.DbContext.Users.Add(new User
        {
            FirstName = "Existing",
            LastName = "User",
            Email = "EXISTING@EXAMPLE.COM",
            PasswordHash = "existing-hash",
            Role = UserRole.Organiser,
            CreatedAt = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Controller.UpdateCurrentProfile(
            new UpdateUserProfileRequest
            {
                FirstName = "Updated",
                LastName = "User",
                Email = "existing@example.com"
            },
            CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public void ProfileEndpointsRequireAuthenticationWithoutRoleRestriction()
    {
        var controllerType = typeof(UsersController);
        Assert.NotNull(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());
        Assert.Null(controllerType.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).SingleOrDefault());
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(RaceDayDbContext dbContext, UsersController controller, User user)
        {
            DbContext = dbContext;
            Controller = controller;
            User = user;
        }

        public RaceDayDbContext DbContext { get; }

        public UsersController Controller { get; }

        public User User { get; }

        public static TestFixture Create(UserRole role, bool authenticated = true)
        {
            var options = new DbContextOptionsBuilder<RaceDayDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new RaceDayDbContext(options);
            var user = new User
            {
                FirstName = "Test",
                LastName = "User",
                Email = "USER@EXAMPLE.COM",
                PasswordHash = "hashed-password",
                Role = role,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
            dbContext.SaveChanges();

            var controller = new UsersController(dbContext);
            if (authenticated)
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            new[] { new Claim("UserID", user.UserID.ToString()) },
                            authenticationType: "TestAuth"))
                    }
                };
            }

            return new TestFixture(dbContext, controller, user);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
