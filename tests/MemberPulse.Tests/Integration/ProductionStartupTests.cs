using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace MemberPulse.Tests.Integration;

/// <summary>
/// Starts the app the way the container does: Production environment, nothing from appsettings.Development.json,
/// everything supplied as configuration, and the schema created by Database:MigrateOnStartup.
/// </summary>
public sealed class ProductionLikeFactory : RealInfrastructureFactory
{
    public const string AdminEmail = "root@example.com";
    public const string AdminPassword = "Production-Admin-9!";

    protected override void ConfigureOverrides(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "production-like-signing-key-0123456789abcdef",
            ["Database:MigrateOnStartup"] = "true",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["Kafka:BootstrapServers"] = "", // handled in-process: this test is about startup, not Kafka
        }));
    }
}

public class ProductionStartupTests(ProductionLikeFactory factory) : IClassFixture<ProductionLikeFactory>
{
    [IntegrationFact]
    public async Task A_fresh_database_is_migrated_the_first_admin_is_created_and_the_dev_seed_data_is_not()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ProductionLikeFactory.AdminEmail, password = ProductionLikeFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var token = (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var members = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/members");
        Assert.Equal(0, members.GetProperty("totalCount").GetInt32()); // no Sarah, Daniel and Maria in production

        // The development-only default credentials must not exist here.
        var devLogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "admin@memberpulse.local", password = "Admin_Dev123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, devLogin.StatusCode);

        // OpenAPI is a development tool.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
