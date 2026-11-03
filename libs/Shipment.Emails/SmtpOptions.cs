using System.ComponentModel.DataAnnotations;

namespace Shipment.Emails;

/// <summary>Outgoing mail (MailPit locally). Bound from "Smtp", validated at start.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public required string Host { get; init; }

    [Range(1, 65535)]
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global - bound from configuration (Smtp:Port).
    public int Port { get; init; } = 25;

    [Required, EmailAddress]
    public required string From { get; init; }

    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global - bound from configuration (Smtp:FromName).
    public string FromName { get; init; } = "Shipment";

    /// <summary>STARTTLS when the server offers it; off for MailPit, on for a real relay.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global - bound from configuration (Smtp:UseStartTls).
    public bool UseStartTls { get; init; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Global - bound from configuration (Smtp:UserName).
    public string? UserName { get; init; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Global - bound from configuration (Smtp:Password).
    public string? Password { get; init; }
}
