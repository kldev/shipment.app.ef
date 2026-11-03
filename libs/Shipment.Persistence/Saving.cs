using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shipment.Domain.Common;

namespace Shipment.Persistence;

public static class Saving
{
    /// <summary>
    /// SaveChanges with the races turned into refusals: someone saved the row meanwhile (xmin moved) - 409 with
    /// "<paramref name="what"/> was changed by someone else"; a unique value taken meanwhile - 409.
    /// </summary>
    public static async Task<Result> SaveAsync(
        this ShipmentDbContext db,
        string what,
        CancellationToken ct
    )
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Stale(what);
        }
        catch (DbUpdateException e)
            when (e.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.StringDataRightTruncation }
            )
        {
            return Error.Validation("A text is longer than its field allows - shorten it.");
        }
        catch (DbUpdateException e)
            when (e.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.CheckViolation }
            )
        {
            return Error.Validation($"{what} has a value the database does not allow.");
        }
        catch (DbUpdateException e)
            when (e.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            )
        {
            return Error.Conflict(
                $"{what} conflicts with a change saved at the same moment. Reload it and try again."
            );
        }
    }

    /// <summary>
    /// The row version the caller read becomes the one EF checks at UPDATE, so a save between the read and this
    /// request is a conflict. False (and nothing set) when it already differs.
    /// </summary>
    public static bool Expect<T>(this ShipmentDbContext db, T entity, uint version)
        where T : Audited
    {
        if (entity.Version != version)
            return false;
        db.Entry(entity).Property(e => e.Version).OriginalValue = version;
        return true;
    }
}
