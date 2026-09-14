using EReader_API.Application.Identity;
using Microsoft.Extensions.Logging;

namespace EReader_API.Infra.Identity
{
    public class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
    {
        public Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct)
        {
            logger.LogInformation("Reset de senha para {Email}: token={Token}", email, resetToken);
            return Task.CompletedTask;
        }
    }
}
