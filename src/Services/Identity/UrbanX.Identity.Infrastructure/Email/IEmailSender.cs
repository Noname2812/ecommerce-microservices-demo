namespace UrbanX.Identity.Infrastructure.Email
{
    public interface IEmailSender
    {
        Task SendEmailConfirmationAsync(string toEmail, string displayName, string confirmationLink, CancellationToken cancellationToken);
        Task SendPasswordResetAsync(string toEmail, string displayName, string resetLink, CancellationToken cancellationToken);
        Task SendWelcomeAsync(string toEmail, string displayName, CancellationToken cancellationToken);
    }
}
