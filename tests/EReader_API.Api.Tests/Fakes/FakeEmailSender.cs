using System.Collections.Concurrent;
using EReader_API.Application.Identity;

namespace EReader_API.Api.Tests.Fakes;

public class FakeEmailSender : IEmailSender
{
    public static readonly ConcurrentDictionary<string, string> CapturedTokens = new();

    public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct)
    {
        CapturedTokens[email] = resetToken;
        return Task.CompletedTask;
    }
}
