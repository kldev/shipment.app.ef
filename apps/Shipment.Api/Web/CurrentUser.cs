using Shipment.Application.Platform;
using Shipment.Domain.Common;

namespace Shipment.Api.Web;

/// <summary>The signed-in user, loaded once per circuit (the shell: sidebar, top bar, role badge).</summary>
public sealed class CurrentUser(PlatformService platform, ICurrentActor actor)
{
    private UserRow? _me;

    /// <summary>The active account of the session; null when it was deactivated meanwhile.</summary>
    public async Task<UserRow?> GetAsync(CancellationToken ct = default)
    {
        if (_me is not null || actor.UserId is not { } id)
            return _me;
        return _me = await platform.GetUserAsync(id, ct);
    }
}

/// <summary>Roles of the operator's people (identity.users.role).</summary>
public static class Roles
{
    public const string Admin = "admin";
    public const string Dispatcher = "dispatcher";
    public const string Courier = "courier";
}
