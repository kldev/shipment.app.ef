namespace Shipment.Tests;

/// <summary>Routes with their ids filled in - the host's constants with the parameter replaced.</summary>
public static class TestRoutes
{
    public static string With(this string route, Guid id) =>
        System.Text.RegularExpressions.Regex.Replace(
            route,
            @"\{[a-zA-Z]+:guid\}",
            id.ToString(),
            default,
            TimeSpan.FromSeconds(1)
        );
}
