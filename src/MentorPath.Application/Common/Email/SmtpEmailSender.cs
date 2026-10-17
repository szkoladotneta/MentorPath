using System.Net;
using System.Net.Mail;
using MentorPath.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.Common.Email;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 25;
    public string? User { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; }
    public string From { get; set; } = "no-reply@mentorpath.local";
}

public sealed class AppOptions
{
    public string PublicBaseUrl { get; set; } = "https://localhost:5001";

    public string BuildUrl(string relative) => $"{PublicBaseUrl.TrimEnd('/')}/{relative.TrimStart('/')}";
}

/// <summary>Transactional account e-mails only (password reset) — D-11.</summary>
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender<AppUser>
{
    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) =>
        SendAsync(email, "MentorPath — potwierdź adres e-mail",
            $"<p>Potwierdź adres e-mail: <a href=\"{confirmationLink}\">potwierdź</a></p>");

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) =>
        SendAsync(email, "MentorPath — reset hasła",
            $"<p>Aby ustawić nowe hasło, kliknij: <a href=\"{resetLink}\">ustaw hasło</a>.</p><p>Jeśli to nie Ty, zignoruj tę wiadomość.</p>");

    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode) =>
        SendAsync(email, "MentorPath — kod resetu hasła", $"<p>Kod resetu hasła: {WebUtility.HtmlEncode(resetCode)}</p>");

    private async Task SendAsync(string to, string subject, string html)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            logger.LogWarning("SMTP is not configured; transactional e-mail was not sent.");
            return;
        }

        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.EnableSsl };
        if (!string.IsNullOrEmpty(o.User)) client.Credentials = new NetworkCredential(o.User, o.Password);
        using var message = new MailMessage(o.From, to, subject, html) { IsBodyHtml = true };
        try
        {
            await client.SendMailAsync(message);
        }
        catch (SmtpException ex)
        {
            logger.LogError(ex, "Failed to send transactional e-mail.");
        }
    }
}
