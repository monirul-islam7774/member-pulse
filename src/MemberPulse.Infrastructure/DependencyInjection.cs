using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MemberPulse.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        bool useInMemoryDatabase = false)
    {
        if (useInMemoryDatabase)
        {
            var databaseName = $"MemberPulse-{Guid.NewGuid()}";
            services.AddDbContext<MemberPulseDbContext>(o => o.UseInMemoryDatabase(databaseName));
        }
        else
        {
            // Resolved when the context is first built, so configuration added after startup
            // (user-secrets, environment, test overrides) is honoured.
            services.AddDbContext<MemberPulseDbContext>((sp, options) =>
            {
                var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("MemberPulse")
                    ?? throw new InvalidOperationException("Connection string 'MemberPulse' is not configured.");

                options.UseSqlServer(connectionString);
            });
        }

        return services;
    }
}