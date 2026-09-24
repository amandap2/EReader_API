using EReader_API.Application.Catalog;
using EReader_API.Application.Identity;
using EReader_API.Infra.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using NSubstitute;

namespace EReader_API.Application.Tests.Identity;

public class AuthServiceTests
{
    private static UserManager<ApplicationUser> MockUserManager() =>
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);

    private static AuthService CreateSut(
        UserManager<ApplicationUser> userManager,
        IJwtTokenGenerator? jwt = null,
        IRefreshTokenStore? refreshTokenStore = null,
        IEmailSender? emailSender = null,
        IBookService? bookService = null) =>
        new(userManager,
            jwt ?? Substitute.For<IJwtTokenGenerator>(),
            refreshTokenStore ?? Substitute.For<IRefreshTokenStore>(),
            emailSender ?? Substitute.For<IEmailSender>(),
            bookService ?? Substitute.For<IBookService>());

    [Fact]
    public async Task LoginAsync_UnknownEmail_ThrowsAuthException()
    {
        var userManager = MockUserManager();
        userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        var sut = CreateSut(userManager);

        var act = () => sut.LoginAsync(new LoginRequest("nobody@test.local", "whatever1"), default);

        await act.Should().ThrowAsync<AuthException>();
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsAuthException()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "a@test.local", DisplayName = "A" };
        var userManager = MockUserManager();
        userManager.FindByEmailAsync(user.Email).Returns(user);
        userManager.CheckPasswordAsync(user, Arg.Any<string>()).Returns(false);

        var sut = CreateSut(userManager);

        var act = () => sut.LoginAsync(new LoginRequest(user.Email, "wrong-password"), default);

        await act.Should().ThrowAsync<AuthException>();
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsIssuedTokens()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "a@test.local", DisplayName = "A" };
        var userManager = MockUserManager();
        userManager.FindByEmailAsync(user.Email).Returns(user);
        userManager.CheckPasswordAsync(user, "correct-password").Returns(true);

        var jwt = Substitute.For<IJwtTokenGenerator>();
        jwt.Generate(user.Id, user.Email, user.DisplayName).Returns(("access-token", 900));

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.CreateAsync(user.Id, Arg.Any<CancellationToken>()).Returns("issued-refresh-token");

        var sut = CreateSut(userManager, jwt, refreshTokenStore);

        var result = await sut.LoginAsync(new LoginRequest(user.Email, "correct-password"), default);

        result.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("issued-refresh-token");
        result.ExpiresInSeconds.Should().Be(900);
    }

    [Fact]
    public async Task RefreshAsync_RevokedToken_RevokesAllForUserAndThrows()
    {
        var userId = Guid.NewGuid();
        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByTokenAsync("reused-token", Arg.Any<CancellationToken>())
            .Returns(new StoredRefreshToken(userId, IsActive: false, WasRevoked: true));

        var sut = CreateSut(MockUserManager(), refreshTokenStore: refreshTokenStore);

        var act = () => sut.RefreshAsync("reused-token", default);

        await act.Should().ThrowAsync<AuthException>();
        await refreshTokenStore.Received(1).RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_NotFound_ThrowsWithoutRevokingAll()
    {
        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByTokenAsync("unknown-token", Arg.Any<CancellationToken>())
            .Returns((StoredRefreshToken?)null);

        var sut = CreateSut(MockUserManager(), refreshTokenStore: refreshTokenStore);

        var act = () => sut.RefreshAsync("unknown-token", default);

        await act.Should().ThrowAsync<AuthException>();
        await refreshTokenStore.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesAndRevokesOldToken()
    {
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, Email = "a@test.local", DisplayName = "A" };

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByTokenAsync("old-token", Arg.Any<CancellationToken>())
            .Returns(new StoredRefreshToken(userId, IsActive: true, WasRevoked: false));
        refreshTokenStore.CreateAsync(userId, Arg.Any<CancellationToken>()).Returns("new-token");

        var userManager = MockUserManager();
        userManager.FindByIdAsync(userId.ToString()).Returns(user);

        var jwt = Substitute.For<IJwtTokenGenerator>();
        jwt.Generate(userId, user.Email, user.DisplayName).Returns(("access-token", 900));

        var sut = CreateSut(userManager, jwt, refreshTokenStore);

        var result = await sut.RefreshAsync("old-token", default);

        result.RefreshToken.Should().Be("new-token");
        await refreshTokenStore.Received(1).RevokeAsync("old-token", "new-token", Arg.Any<CancellationToken>());
    }
}
