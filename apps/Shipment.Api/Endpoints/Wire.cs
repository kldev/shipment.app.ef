using System.Text.Json;
using JetBrains.Annotations;

namespace Shipment.Api.Endpoints;

/// <summary>
/// An enum in the query string as the API writes it in JSON - its snake_case name (<c>?status=courier_assigned</c>).
/// Minimal APIs bind it through <see cref="TryParse"/>; an unknown name is a 400 before the handler.
/// </summary>
public readonly record struct Wire<T>(T Value)
    where T : struct, Enum
{
    private static readonly Dictionary<string, T> Values = Enum.GetValues<T>()
        .ToDictionary(
            v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()),
            v => v,
            StringComparer.Ordinal
        );

    [UsedImplicitly] // Minimal APIs bind the parameter through this static method.
    public static bool TryParse(string? text, out Wire<T> wire)
    {
        var found = Values.TryGetValue(text ?? "", out var value);
        wire = new Wire<T>(value);
        return found;
    }
}
