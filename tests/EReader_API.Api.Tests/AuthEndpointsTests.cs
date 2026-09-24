using System.Net;
using System.Net.Http.Json;
using EReader_API.Api.Tests.Fixtures;
using EReader_API.Api.Tests.Helpers;
using EReader_API.Application.Identity;
using FluentAssertions;

namespace EReader_API.Api.Tests;

public class AuthEndpointsTests(EReaderApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Register_ThenGetMe_ReturnsMatchingProfile()
    {
        var authed = await AuthHelper.RegisterAndLoginAsync(Client);

        var me = await Client.GetAsync("/api/auth/me");

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await me.Content.ReadFromJsonAsync<UserProfile>();
        profile!.Id.Should().Be(authed.UserId);
        profile.Email.Should().Be(authed.Email);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var register = await Client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Test1234!", "Test User"));
        register.EnsureSuccessStatusCode();

        var login = await Client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "wrong-password"));

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_RotatesToken()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var register = await Client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Test1234!", "Test User"));
        var original = await register.Content.ReadFromJsonAsync<AuthResult>();

        var refresh = await Client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(original!.RefreshToken));

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await refresh.Content.ReadFromJsonAsync<AuthResult>();
        rotated!.RefreshToken.Should().NotBe(original.RefreshToken);

        // token antigo já foi revogado pela rotação
        var reuse = await Client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshRequest(original.RefreshToken));
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
