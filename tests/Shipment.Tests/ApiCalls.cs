using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Shipment.Tests;

/// <summary>The REST API's calls as the tests make them: reads, writes and their refusals (ProblemDetails).</summary>
public static class ApiCalls
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A list (a JSON array).</summary>
    public static async Task<JsonArray> ListAsync(this HttpClient client, string path) =>
        (await client.GetFromJsonAsync<JsonArray>(path, Ct))!;

    /// <summary>One object; the call must succeed.</summary>
    public static async Task<JsonObject> GetObjectAsync(this HttpClient client, string path) =>
        (await client.GetFromJsonAsync<JsonObject>(path, Ct))!;

    /// <summary>A write that must succeed (2xx); its JSON answer.</summary>
    private static async Task<JsonObject> SendAsync(
        this HttpClient client,
        HttpMethod method,
        string path,
        object body
    )
    {
        var response = await client.SendAsync(
            new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) },
            Ct
        );
        if (!response.IsSuccessStatusCode)
            Assert.Fail(
                $"{method} {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}"
            );
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!;
    }

    public static Task<JsonObject> PostAsync(this HttpClient client, string path, object body) =>
        client.SendAsync(HttpMethod.Post, path, body);

    public static Task<JsonObject> PutAsync(this HttpClient client, string path, object body) =>
        client.SendAsync(HttpMethod.Put, path, body);

    /// <summary>A write that must be refused with this status and a detail that contains <paramref name="message"/>.</summary>
    public static async Task RefusedAsync(
        this HttpClient client,
        HttpMethod method,
        string path,
        object body,
        HttpStatusCode status,
        string message
    )
    {
        var response = await client.SendAsync(
            new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) },
            Ct
        );
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(
            response.StatusCode == status,
            $"{method} {path}: expected {(int)status} '{message}', got {(int)response.StatusCode} {text}"
        );
        Assert.Contains(message, JsonNode.Parse(text)!["detail"]!.GetValue<string>());
    }

    /// <summary>
    /// A body the API cannot bind (an unknown enum name, null for a value that cannot be null): 400 before any service
    /// rule, so no message of ours to check.
    /// </summary>
    public static async Task NotBoundAsync(
        this HttpClient client,
        HttpMethod method,
        string path,
        object body
    )
    {
        var response = await client.SendAsync(
            new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) },
            Ct
        );
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"{method} {path}: expected 400, got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}"
        );
    }

    /// <summary>The row version a write answers with (xmin).</summary>
    public static uint RowVersion(this JsonObject result) => result["rowVersion"]!.GetValue<uint>();
}
