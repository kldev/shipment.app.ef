using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Shipment.Persistence;

/// <summary>An enum stored as its snake_case name: ShipmentStatus.CourierAssigned is 'courier_assigned'.</summary>
// Created by EF Core (HaveConversion<SnakeCaseEnumConverter<T>> in ShipmentDbContext.ConfigureConventions).
[UsedImplicitly]
public sealed partial class SnakeCaseEnumConverter<TEnum>()
    : ValueConverter<TEnum, string>(value => ToName(value), name => FromName(name))
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> Names = Enum.GetValues<TEnum>()
        .ToDictionary(value => value, value => Snake(value.ToString()));

    private static readonly Dictionary<string, TEnum> Values = Names.ToDictionary(
        pair => pair.Value,
        pair => pair.Key,
        StringComparer.Ordinal
    );

    public static string ToName(TEnum value) => Names[value];

    private static TEnum FromName(string name) =>
        Values.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"'{name}' is not a {typeof(TEnum).Name}.");

    private static string Snake(string name) =>
        UpperAfterLower().Replace(name, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex UpperAfterLower();
}
