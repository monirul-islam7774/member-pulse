using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace MemberPulse.Tests.Api;

/// <summary>The app with a built UI present: a temp web root holding index.html and a script.</summary>
public sealed class SpaApiFactory : MemberPulseApiFactory
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"memberpulse-spa-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html><body>spa-shell</body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "app.js"), "console.log('spa-script');");
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "index-abc123.js"), "console.log('hashed');");

        base.ConfigureWebHost(builder);
        builder.UseWebRoot(_webRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true);
    }
}

public class SpaHostingTests(SpaApiFactory factory) : IClassFixture<SpaApiFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/campaigns/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("/members")]
    public async Task Client_routes_return_index_html_to_anonymous_users(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("spa-shell", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/campaigns/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    public async Task The_page_itself_is_always_revalidated_so_a_deploy_never_leaves_a_stale_shell_pointing_at_deleted_assets(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache, $"Cache-Control was '{response.Headers.CacheControl}'");
    }

    [Fact]
    public async Task Hashed_build_assets_are_cached_for_a_year_because_their_names_change_with_their_content()
    {
        var response = await factory.CreateClient().GetAsync("/assets/index-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = response.Headers.CacheControl!.ToString();
        Assert.Contains("max-age=31536000", cacheControl);
        Assert.Contains("immutable", cacheControl);
    }

    [Fact]
    public async Task Other_static_files_are_not_given_a_long_cache_lifetime()
    {
        var response = await factory.CreateClient().GetAsync("/app.js");

        Assert.NotEqual(TimeSpan.FromDays(365), response.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task Static_files_are_served_to_anonymous_users()
    {
        var response = await factory.CreateClient().GetAsync("/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("spa-script", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_api_paths_are_not_swallowed_by_the_fallback()
    {
        var anonymous = await factory.CreateClient().GetAsync("/api/nope");
        var signedIn = await factory.CreateAuthenticatedClient().GetAsync("/api/nope");
        var upperCase = await factory.CreateAuthenticatedClient().GetAsync("/API/nope");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, upperCase.StatusCode);
        Assert.DoesNotContain("spa-shell", await signedIn.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_files_with_an_extension_are_404_not_index_html()
    {
        var response = await factory.CreateClient().GetAsync("/missing.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_the_api_still_work()
    {
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateAuthenticatedClient().GetAsync("/api/members")).StatusCode);
    }
}

public class SpaWithoutBuiltUiTests(MemberPulseApiFactory factory) : IClassFixture<MemberPulseApiFactory>
{
    [Fact]
    public async Task Without_a_built_ui_client_routes_are_404()
    {
        var response = await factory.CreateClient().GetAsync("/campaigns/abc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
