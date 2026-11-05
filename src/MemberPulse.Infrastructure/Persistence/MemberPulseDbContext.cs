using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Infrastructure.Persistence;

public sealed class MemberPulseDbContext(DbContextOptions<MemberPulseDbContext> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MemberPulseDbContext).Assembly);
    }
}