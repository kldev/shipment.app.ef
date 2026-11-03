using System.Net;
using System.Text.Json.Nodes;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>The REST API as a whole: its OpenAPI document, the sign-in and the session it needs.</summary>
[Collection(ApiCollection.Name)]
public sealed class ApiTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FormUrlEncodedContent SignIn(string username, string password) =>
        new(new Dictionary<string, string> { ["username"] = username, ["password"] = password });

    [Fact, Trait("verify", "platform")]
    public async Task The_openapi_document_describes_every_module()
    {
        var doc = JsonNode.Parse(
            await api.CreateClient().GetStringAsync($"/openapi/{ApiRoutes.Document}.json", Ct)
        )!;
        Assert.Equal("Shipment API", doc["info"]!["title"]!.GetValue<string>());
        var paths = doc["paths"]!.AsObject();

        foreach (
            var path in new[]
            {
                "/api/v1/me",
                "/api/v1/users",
                "/api/v1/partners",
                "/api/v1/partners/{partnerId}",
                "/api/v1/customers/{customerId}/addresses",
                "/api/v1/products",
                "/api/v1/products/{productId}/listed",
                "/api/v1/couriers/{courierId}/start-shift",
                "/api/v1/courier-clearance",
                "/api/v1/age-check",
                "/api/v1/orders",
                "/api/v1/orders/{orderId}/picking/finish",
                "/api/v1/picking-history",
                "/api/v1/shipments/{shipmentId}/assign",
                "/api/v1/shipments/{shipmentId}/tracking",
                "/api/v1/billing/entries",
            }
        )
            Assert.True(paths.ContainsKey(path), $"{path} is not in the OpenAPI document");
        // Writes document their refusals.
        var create = paths["/api/v1/partners"]!["post"]!["responses"]!.AsObject();
        Assert.True(create.ContainsKey("201"));
        Assert.True(create.ContainsKey("409"));
    }

    [Fact, Trait("verify", "platform")]
    public async Task Sign_in_rejects_a_wrong_password_and_the_api_needs_a_session()
    {
        var browser = api.Browser();
        foreach (
            var (username, password) in new[]
            {
                (ApiFixture.Admin, "nope"),
                ("nobody", ApiFixture.Password),
                // The e-mail is for notifications only, never a way in.
                ("k.lopuch@ambitsys.eu", ApiFixture.Password),
            }
        )
        {
            var response = await browser.PostAsync("/auth/login", SignIn(username, password), Ct);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/sign-in?error=invalid", response.Headers.Location!.OriginalString);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await browser.GetAsync(ApiRoutes.Me, Ct)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await browser.GetAsync(ApiRoutes.Partners, Ct)).StatusCode
        );
    }

    [Fact, Trait("verify", "platform")]
    public async Task An_inactive_account_cannot_sign_in()
    {
        var username = await api.AddAdminAsync($"left.{Guid.NewGuid():N}"[..20], "Left Already");
        await api.ExecuteAsync(
            $"UPDATE identity.users SET status = 'inactive' WHERE username = '{username}'"
        );

        var response = await api.Browser()
            .PostAsync("/auth/login", SignIn(username, ApiFixture.Password), Ct);
        Assert.StartsWith("/sign-in?error=inactive", response.Headers.Location!.OriginalString);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact, Trait("verify", "platform")]
    public async Task The_demo_sign_in_opens_a_session_for_the_api()
    {
        // DemoSignIn is on in appsettings.Development (the tests' environment): no password, the default admin.
        var browser = api.Browser();
        var login = await browser.PostAsync(
            "/auth/demo-login",
            new FormUrlEncodedContent(new Dictionary<string, string>()),
            Ct
        );
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/dashboard", login.Headers.Location!.OriginalString);

        var me = await browser.GetObjectAsync(ApiRoutes.Me);
        Assert.Equal(ApiFixture.Admin, me["username"]!.GetValue<string>());
        Assert.Equal(ApiFixture.AdminId, me["id"]!.GetValue<Guid>());
    }

    [Fact, Trait("verify", "platform")]
    public async Task Me_has_the_username_and_the_notification_email_apart()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);

        var me = await admin.GetObjectAsync(ApiRoutes.Me);
        Assert.Equal("k.lopuch", me["username"]!.GetValue<string>());
        Assert.Equal("k.lopuch@ambitsys.eu", me["email"]!.GetValue<string>());
        Assert.Equal("admin", me["role"]!.GetValue<string>());
        Assert.Equal("active", me["status"]!.GetValue<string>());

        var users = await admin.ListAsync(ApiRoutes.Users);
        Assert.Contains(users, u => u!["username"]!.GetValue<string>() == "k.lopuch");
    }
}
