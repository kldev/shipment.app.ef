using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shipment.Domain.Platform;
using Shipment.Persistence;

namespace Shipment.Application.Platform;

public sealed record UserRow(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string Role,
    UserStatus Status,
    DateTimeOffset CreatedAt
);

/// <summary>Who signs in, as the session needs them.</summary>
public sealed record SignInUser(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string Role
);

public enum SignInFailure
{
    None,
    WrongCredentials,
    Inactive,
}

/// <summary>The operator's people and the sign-in (ADR 0006): a username and the shared password (ADR 0011).</summary>
public sealed class PlatformService(IDbContextFactory<ShipmentDbContext> factory)
{
    // Verified when the username is unknown, so both answers take the same time (no account enumeration).
    private const string DummyHash =
        "AQAAAAEAAYagAAAAEHMfTasCGfhuoqd0wBdrkW+GGIa4qMIY79qMRZPWVUy9zUeLcF7H2N9EvUQ0Zi/y5A==";

    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<UserRow?> GetUserAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db
            .Users.Where(u => u.Id == id && u.Status == UserStatus.Active)
            .Select(Row)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<UserRow>> ListUsersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.OrderBy(u => u.DisplayName).Select(Row).ToListAsync(ct);
    }

    /// <summary>Username and password; an inactive account is told apart only after the password matched.</summary>
    public async Task<(SignInUser? User, SignInFailure Failure)> VerifyAsync(
        string? username,
        string? password,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return (null, SignInFailure.WrongCredentials);
        var user = await FindAsync(username, ct);
        if (user is null)
        {
            Hasher.VerifyHashedPassword(null!, DummyHash, password);
            return (null, SignInFailure.WrongCredentials);
        }
        if (
            Hasher.VerifyHashedPassword(user, user.PasswordHash ?? DummyHash, password)
                == PasswordVerificationResult.Failed
            || user.PasswordHash is null
        )
            return (null, SignInFailure.WrongCredentials);
        return user.Status == UserStatus.Active
            ? (ToSignIn(user), SignInFailure.None)
            : (null, SignInFailure.Inactive);
    }

    /// <summary>The active account with this username, no password - the local demo's sign-in only.</summary>
    public async Task<SignInUser?> FindActiveAsync(
        string username,
        CancellationToken ct = default
    ) =>
        await FindAsync(username, ct) is { Status: UserStatus.Active } user ? ToSignIn(user) : null;

    private async Task<User?> FindAsync(string username, CancellationToken ct)
    {
        var name = username.Trim().ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == name, ct);
    }

    private static SignInUser ToSignIn(User user) =>
        new(user.Id, user.Username, user.Email, user.DisplayName, user.Role);

    private static readonly System.Linq.Expressions.Expression<Func<User, UserRow>> Row =
        u => new UserRow(u.Id, u.Username, u.Email, u.DisplayName, u.Role, u.Status, u.CreatedAt);
}
