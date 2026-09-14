namespace EReader_API.Application.Identity;

public interface IEmailSender
{
    Task SendPasswordResetAsync(string email, string resetToken, CancellationToken ct);
}
