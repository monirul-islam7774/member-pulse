namespace MemberPulse.Tests.Integration;

/// <summary>
/// A test that needs the real SQL Server and Kafka from docker-compose. Skipped unless
/// MEMBERPULSE_INTEGRATION_TESTS=true, so a plain <c>dotnet test</c> stays fast and needs no Docker.
/// </summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "MEMBERPULSE_INTEGRATION_TESTS";

    public IntegrationFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Set {EnvironmentVariable}=true (with SQL Server and Kafka running) to run integration tests.";
        }
    }
}
