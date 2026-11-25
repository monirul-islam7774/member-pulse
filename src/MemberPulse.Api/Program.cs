using System.Text.Json.Serialization;
using MemberPulse.Api.Auth;
using MemberPulse.Api.Hosting;
using MemberPulse.Api.Messaging;
using MemberPulse.Infrastructure;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Register services (the DI container)
var isTesting = builder.Environment.IsEnvironment("Testing");
builder.Services.AddInfrastructure(useInMemoryDatabase: isTesting);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddJwtAuth();
builder.Services.AddMessaging();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<MemberPulseDbContext>();

var app = builder.Build();

// 2. Startup tasks
var isDevelopment = app.Environment.IsDevelopment();

// Development always brings the database up to date. Elsewhere it's opt-in (Database:MigrateOnStartup=true), which
// is what the container image uses; with several instances, prefer running migrations as a separate deploy step.
if (isDevelopment || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MemberPulseDbContext>();
    await db.Database.MigrateAsync();

    if (isDevelopment)
    {
        await DevDataSeeder.SeedAsync(db);
    }
}

if (isDevelopment)
{
    app.MapOpenApi().AllowAnonymous();
}

// Creates the first admin if Seed:AdminEmail/AdminPassword are configured and no staff exist yet.
await app.Services.BootstrapAdminAsync();

// 3. HTTP pipeline (middleware, runs in this order)
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSpaStaticFiles(); // before auth: anonymous visitors must be able to load the page with the login form
app.UseRouting();        // explicitly, after static files: otherwise the SPA fallback matches /app.js and files are never served

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapSpaFallback();

app.Run();

public partial class Program { }