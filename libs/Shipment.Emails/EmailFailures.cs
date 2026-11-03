using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Shipment.Emails;

/// <summary>Why an e-mail did not go out, in words a person can act on - never the address or the server's text.</summary>
public enum EmailFailureKind
{
    /// <summary>The server refused the recipient (5xx at RCPT TO), or the address is malformed - usually a typo.</summary>
    AddressRejected = 1,

    /// <summary>No connection, a timeout, a broken session, or a temporary (4xx) refusal - trying later may help.</summary>
    ServerUnavailable = 2,

    /// <summary>The server refused Shipment's credentials - the SMTP settings need a look.</summary>
    AuthenticationFailed = 3,

    Other = 4,
}

/// <summary>
/// Sorts the exceptions of <see cref="IEmailSender"/> into <see cref="EmailFailureKind"/> (MailKit and MimeKit). A
/// server that accepted the message says only that it took it, not that it arrived - a later bounce is not seen here.
/// </summary>
public static class EmailFailures
{
    public static EmailFailureKind Classify(Exception exception) =>
        exception switch
        {
            SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } refused => (int)
                refused.StatusCode >= 500
                ? EmailFailureKind.AddressRejected
                : EmailFailureKind.ServerUnavailable,
            ParseException => EmailFailureKind.AddressRejected,
            AuthenticationException => EmailFailureKind.AuthenticationFailed,
            SocketException
            or IOException
            or TimeoutException
            or SmtpProtocolException
            or SslHandshakeException
            or ServiceNotConnectedException => EmailFailureKind.ServerUnavailable,
            _ => EmailFailureKind.Other,
        };
}
