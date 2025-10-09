using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using DocumentFormat.OpenXml.Vml;
using System.Net.Mail;

namespace GittBilSmsCore.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        public SmtpEmailSender(IConfiguration config) => _config = config;

        public async Task SendEmailAsync(string to, string subject, string htmlBody)
        {
            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(_config["Smtp:From"]));
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = subject;
            msg.Body = new TextPart("html") { Text = htmlBody };

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(
                _config["Smtp:Host"],
                int.Parse(_config["Smtp:Port"]),
                SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(
                _config["Smtp:User"],
                _config["Smtp:Pass"]);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
        }
    }
}