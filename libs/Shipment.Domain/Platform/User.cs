namespace Shipment.Domain.Platform;

// Inactive is set in the database by an admin of the server (no UI yet); the sign-in reads it.
[JetBrains.Annotations.UsedImplicitly(JetBrains.Annotations.ImplicitUseTargetFlags.Members)]
public enum UserStatus
{
    Active,
    Inactive,
}

/// <summary>
/// One of the operator's people (identity.users). Everyone is an admin who signs in with a username; the e-mail is for
/// notifications only (ADR 0006). The password hash is ASP.NET Core's PasswordHasher format; null = no password sign-in.
/// </summary>
public sealed class User
{
    public const string AdminRole = "admin";

    public Guid Id { get; private set; }

    public string Username { get; private set; } = "";

    public string Email { get; private set; } = "";

    public string? PasswordHash { get; private set; }

    public string DisplayName { get; private set; } = "";

    public string Role { get; private set; } = AdminRole;

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
