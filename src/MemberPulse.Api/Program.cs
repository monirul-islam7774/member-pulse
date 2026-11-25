using System.Text.Json.Serialization;
using MemberPulse.Api.Auth;
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

// 2. Startup tasks (Development only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MemberPulseDbContext>();
    await db.Database.MigrateAsync();
    await DevDataSeeder.SeedAsync(db);
}

// Creates the first admin if Seed:AdminEmail/AdminPassword are configured and no staff exist yet.
await app.Services.BootstrapAdminAsync();

// 3. HTTP pipeline (middleware, runs in this order)
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program { }