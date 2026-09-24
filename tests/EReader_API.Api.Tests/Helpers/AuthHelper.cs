using System.Net.Http.Headers;
using System.Net.Http.Json;
using EReader_API.Application.Identity;

namespace EReader_API.Api.Tests.Helpers;

public static class AuthHelper
{
    public record AuthedUser(Guid UserId, string Email);

    /// <summary>
    /// Registra um usuário único, seta o Bearer no cliente e devolve o userId (via GET /me,
    /// já que AuthResult não carrega o id).
    /// </summary>
    public static async Task<AuthedUser> RegisterAndLoginAsync(HttpClient client, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@test.local";

        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            email, "Test1234!", "Test User"));
        register.EnsureSuccessStatusCode();
        var tokens = await register.Content.ReadFromJsonAsync<AuthResult>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var me = await client.GetFromJsonAsync<UserProfile>("/api/auth/me");
        return new AuthedUser(me!.Id, email);
    }
}
