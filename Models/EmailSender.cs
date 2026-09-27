using Microsoft.AspNetCore.Identity.UI.Services;

namespace SubBill.Models
{
    public class EmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // Implement your email sending logic here
            // For example, using SMTP or a third-party email service
            return Task.CompletedTask;
        }
    }
}
