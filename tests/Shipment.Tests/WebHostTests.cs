using System.Net;
using Shipment.Api.Endpoints;

namespace Shipment.Tests;

/// <summary>The one host serves the Blazor front next to the REST API (ADR 0002): what is public and what is not.</summary>
[Trait("verify", "web")]
[Collection(ApiCollection.Name)]
public sealed class WebHostTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sign_in_page_and_its_assets_are_public_the_rest_redirects_to_it()
    {
        var browser = api.Browser();

        var signIn = await browser.GetAsync("/sign-in", Ct);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Contains("action=\"auth/login\"", await signIn.Content.ReadAsStringAsync(Ct));

        var css = await browser.GetAsync("/css/app.css", Ct);
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);

        // The framework script: public, and present at all (RequiresAspNetWebAssets in Shipment.Api.csproj).
        var script = await browser.GetAsync("/_framework/blazor.web.js", Ct);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);

        var users = await browser.GetAsync("/users", Ct);
        Assert.Equal(HttpStatusCode.Redirect, users.StatusCode);
        Assert.StartsWith(
            "http://localhost/sign-in?ReturnUrl=%2Fusers",
            users.Headers.Location!.ToString()
        );

        var scalar = await browser.GetAsync("/scalar/", Ct);
        Assert.Equal(HttpStatusCode.Redirect, scalar.StatusCode);
    }

    [Fact]
    public async Task The_demo_button_signs_in_as_the_default_admin_without_a_password()
    {
        // DemoSignIn is on in appsettings.Development (the tests' environment) and in deploy.sh; off by default.
        var browser = api.Browser();
        var page = WebUtility.HtmlDecode(await browser.GetStringAsync("/sign-in", Ct));
        Assert.Contains("action=\"auth/demo-login\"", page);

        var login = await browser.PostAsync(
            "/auth/demo-login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["returnUrl"] = "/orders" }),
            Ct
        );
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/orders", login.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/orders", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_gets_the_pages_and_scalar()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        foreach (
            var page in new[]
            {
                "/users",
                "/partners",
                "/customers",
                "/couriers",
                "/compliance",
                "/orders",
                "/picking",
                "/picking/history",
                "/picking/history?period=today&missing=true",
                "/shipments",
                "/tracking",
                "/billing",
                "/js/map.js",
                "/lib/leaflet/leaflet.js",
            }
        )
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(page, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/scalar/", Ct)).StatusCode);

        var logout = await admin.PostAsync("/auth/logout", null, Ct);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await admin.GetAsync(ApiRoutes.Me, Ct)).StatusCode
        );
    }

    [Fact]
    public async Task Writes_from_another_site_are_refused()
    {
        var admin = await api.AsAsync(ApiFixture.Admin);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task Liveness_is_public_and_an_unknown_api_path_stays_404()
    {
        var browser = api.Browser();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/health", Ct)).StatusCode);
        var admin = await api.AsAsync(ApiFixture.Admin);
        var unknown = await admin.GetAsync("/api/nothing/here", Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.DoesNotContain("<html", await unknown.Content.ReadAsStringAsync(Ct));
    }
}
