using Shipment.Domain.Common;

namespace Shipment.Application.Common;

internal static class Races
{
    /// <summary>
    /// A create with the client's id (ADR 0009) sent twice at once - a double click, a retry after a timeout: the second
    /// insert hits the key. Once more, it finds the first one's row and answers as the first did (or refuses other
    /// values under the same id).
    /// </summary>
    public static async Task<Result<T>> OnceMoreOnConflictAsync<T>(Func<Task<Result<T>>> attempt)
    {
        var result = await attempt();
        return result.Error is { Kind: ErrorKind.Conflict } ? await attempt() : result;
    }
}
