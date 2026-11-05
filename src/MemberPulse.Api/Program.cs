using System.Text.Json.Serialization;
using MemberPulse.Infrastructure;
using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Register services (the DI container)
var isTesting = builder.Environment.IsEnvironment("Testing");
builder.Services.AddInfrastructure(builder.Configuration, useInMemoryDatabase: isTesting);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<MemberPulseDbContext>();

var app = builder.Build();

// 2. Startup tasks (Development only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<MemberPulseDbContext>();
    await db.Database.MigrateAsync();
    await DevDataSeeder.SeedAsync(db);
}

// 3. HTTP pipeline (middleware, runs in this order)
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }