using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace MemberPulse.Tests.Integration;

/// <summary>
/// Runs the whole app as in Development (real migrations, SQL Server, Kafka producer and consumer),
/// but against a throwaway database and a topic/consumer group unique to this run.
/// </summary>
public class RealInfrastructureFactory : WebApplicationFactory<Program>
{
    private readonly string _runId = Guid.NewGuid().ToString("N")[..10];

    public string DatabaseName => $"MemberPulse_it_{_runId}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var server = Environment.GetEnvironmentVariable("MEMBERPULSE_IT_SQL")
            ?? "Server=localhost,1433;User Id=sa;Password=MemberPulse_Dev123!;TrustServerCertificate=True";
        _server = server;
        var kafka = Environment.GetEnvironmentVariable("MEMBERPULSE_IT_KAFKA") ?? "localhost:9092";

        builder.UseEnvironment("Development"); // migrates, seeds, bootstraps the admin from appsettings.Development.json
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MemberPulse"] = $"{server};Database={DatabaseName}",
            ["Kafka:BootstrapServers"] = kafka,
            ["Kafka:TransactionsTopic"] = $"memberpulse.it.{_runId}",
            ["Kafka:ConsumerGroup"] = $"memberpulse-it-{_runId}",
            ["Kafka:PollIntervalMilliseconds"] = "200",
        }));
        ConfigureOverrides(builder);
    }

    /// <summary>Lets a subclass change settings or services after the defaults above.</summary>
    protected virtual void ConfigureOverrides(IWebHostBuilder builder) { }

    private string _server = string.Empty;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); // stop the host first so nothing is still connected

        if (disposing && _server.Length > 0)
        {
            try
            {
                using var master = new SqlConnection(_server + ";Database=master");
                master.Open();
                using var drop = master.CreateCommand();
                drop.CommandText = $"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN " +
                                   $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                                   $"DROP DATABASE [{DatabaseName}]; END";
                drop.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                // best effort: the throwaway database is harmless if it lingers
            }
        }
    }
}
