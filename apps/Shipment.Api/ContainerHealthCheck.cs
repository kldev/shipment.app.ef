namespace Shipment.Api;

/// <summary>
/// <c>dotnet Shipment.Api.dll healthcheck</c>: the aspnet image has no curl, so the Docker HEALTHCHECK calls the app's own
/// <c>/health</c> endpoint through this mode (as in Tech Lead Wiki).
/// </summary>
internal static class ContainerHealthCheck
{
    // One request per process (the probe exits right after it), so one static client.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<int> RunAsync()
    {
        var port =
            Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
        try
        {
            using var response = await Client.GetAsync($"http://localhost:{port}/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
