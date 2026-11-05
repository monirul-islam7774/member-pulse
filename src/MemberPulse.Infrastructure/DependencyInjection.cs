using MemberPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MemberPulse.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryDatabase = false)
    {
        if (useInMemoryDatabase)
        {
            var databaseName = $"MemberPulse-{Guid.NewGuid()}";
            services.AddDbContext<MemberPulseDbContext>(o => o.UseInMemoryDatabase(databaseName));
        }
        else
        {
            var connectionString = configuration.GetConnectionString("MemberPulse")
                ?? throw new InvalidOperationException("Connection string 'MemberPulse' is not configured.");

            services.AddDbContext<MemberPulseDbContext>(o => o.UseSqlServer(connectionString));
        }

        return services;
    }
}