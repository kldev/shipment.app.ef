namespace Shipment.Application.Common;

/// <summary>A page of a list: the rows and whether more follow (read one row more, no count).</summary>
public sealed record Slice<T>(IReadOnlyList<T> Items, bool HasMore);

internal static class Lists
{
    /// <summary>The most rows a list without paging returns.</summary>
    public const int Max = 500;

    public static Slice<T> ToSlice<T>(this List<T> rows, int take) =>
        new(rows.Take(take).ToList(), rows.Count > take);
}
