using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shipment.Domain.Common;

namespace Shipment.Persistence;

/// <summary>Stamps who and when on every added or changed <see cref="Audited"/> row, from the scope's actor.</summary>
public sealed class AuditInterceptor(ICurrentActor actor, TimeProvider time)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is { } context)
            Stamp(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is { } context)
            Stamp(context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext context)
    {
        var now = time.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<Audited>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = actor.UserId;
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.ModifiedAt = now;
                entry.Entity.ModifiedBy = actor.UserId;
            }
        }
    }
}
