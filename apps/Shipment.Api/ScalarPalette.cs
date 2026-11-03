namespace Shipment.Api;

/// <summary>
/// Scalar's dark mode in Shipment's palette (Styles/app.css, the .dark tokens, as hex). Fonts are the
/// system's: nothing is fetched from a CDN.
/// </summary>
internal static class ScalarPalette
{
    public const string Css = """
        .dark-mode {
          --scalar-background-1: #0d131c;
          --scalar-background-2: #121a26;
          --scalar-background-3: #1a2433;
          --scalar-background-accent: #1b2a40;
          --scalar-color-1: #eef1f6;
          --scalar-color-2: #b8c0cc;
          --scalar-color-3: #8e98a8;
          --scalar-color-accent: #66a6ff;
          --scalar-border-color: #253041;
          --scalar-color-green: #6fcf97;
          --scalar-color-red: #f87171;
          --scalar-color-yellow: #e8c06a;
          --scalar-color-blue: #66a6ff;
          --scalar-color-orange: #f2a65a;
          --scalar-color-purple: #b39ddb;
          --scalar-sidebar-background-1: #0a1018;
          --scalar-sidebar-color-1: #eef1f6;
          --scalar-sidebar-color-2: #8e98a8;
          --scalar-sidebar-border-color: #1c2532;
          --scalar-sidebar-item-hover-background: #172231;
          --scalar-sidebar-item-active-background: #1b2a40;
          --scalar-sidebar-color-active: #66a6ff;
        }
        """;
}
