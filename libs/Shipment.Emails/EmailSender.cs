using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Shipment.Emails;

/// <summary>
/// One outgoing message: plain text always, HTML as the nicer alternative; ReplyTo when an answer should go to someone
/// else than the sender (an early-access request from the product page goes back to the person who sent it).
/// </summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string Text,
    string Html,
    string? ReplyTo = null
);

/// <summary>The only way Shipment sends mail - a seam tests replace to read what would have gone out.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

internal sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger
) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        if (message.ReplyTo is { } replyTo)
            mime.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            TextBody = message.Text,
            HtmlBody = message.Html,
        }.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            ct
        );
        if (!string.IsNullOrEmpty(_options.UserName))
            await smtp.AuthenticateAsync(_options.UserName, _options.Password ?? "", ct);
        await smtp.SendAsync(mime, ct);
        await smtp.DisconnectAsync(true, ct);

        // The address is personal data - the log says that a message went out and which kind, not to whom.
        logger.LogInformation("E-mail sent: {Subject}", message.Subject);
    }
}

public static class EmailSetup
{
    public static IServiceCollection AddEmail(this IServiceCollection services)
    {
        services
            .AddOptions<SmtpOptions>()
            .BindConfiguration(SmtpOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<EmailTemplates>();
        return services;
    }
}
