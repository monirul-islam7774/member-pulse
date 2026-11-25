using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Campaigns;
using MemberPulse.Domain.Members;
using MemberPulse.Domain.Segmentation;
using MemberPulse.Domain.Staff;
using MemberPulse.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Infrastructure.Persistence;

public sealed class MemberPulseDbContext(DbContextOptions<MemberPulseDbContext> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignDelivery> CampaignDeliveries => Set<CampaignDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MemberPulseDbContext).Assembly);
    }
}