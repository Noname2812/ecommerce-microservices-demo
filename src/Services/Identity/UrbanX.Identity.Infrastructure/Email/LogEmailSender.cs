using Microsoft.Extensions.Logging;

namespace UrbanX.Identity.Infrastructure.Email
{
    public sealed class LogEmailSender : IEmailSender
    {
        private readonly ILogger<LogEmailSender> _logger;

        public LogEmailSender(ILogger<LogEmailSender> logger) => _logger = logger;

        public Task SendEmailConfirmationAsync(string toEmail, string displayName, string confirmationLink, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[EMAIL][ConfirmEmail] To={To} Name={Name} Link={Link}",
                toEmail, displayName, confirmationLink);
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(string toEmail, string displayName, string resetLink, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[EMAIL][ResetPassword] To={To} Name={Name} Link={Link}",
                toEmail, displayName, resetLink);
            return Task.CompletedTask;
        }

        public Task SendWelcomeAsync(string toEmail, string displayName, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[EMAIL][Welcome] To={To} Name={Name}",
                toEmail, displayName);
            return Task.CompletedTask;
        }
    }
}
