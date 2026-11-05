using MemberPulse.Domain.Accounts;
using MemberPulse.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace MemberPulse.Infrastructure.Persistence;

public static class DevDataSeeder
{
    public static async Task SeedAsync(MemberPulseDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Members.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        var sarah = Member.Create("Sarah", "Johnson", "sarah.johnson@example.com", new DateOnly(1994, 4, 12), now.AddYears(-3));
        sarah.OpenAccount(AccountType.Savings, 8_500m, now.AddYears(-3));
        var checking = sarah.OpenAccount(AccountType.Checking, 4_200m, now.AddYears(-2));
        checking.Post(-2_150.75m, "Lowe's Home Improvement", "HomeImprovement", now.AddDays(-2));

        var daniel = Member.Create("Daniel", "Kim", "daniel.kim@example.com", new DateOnly(1988, 9, 30), now.AddYears(-6));
        daniel.OpenAccount(AccountType.Checking, 2_300m, now.AddYears(-6));
        daniel.OpenAccount(AccountType.AutoLoan, 0m, now.AddYears(-1));

        var maria = Member.Create("Maria", "Lopez", "maria.lopez@example.com", new DateOnly(2000, 1, 5), now.AddMonths(-4));
        maria.OpenAccount(AccountType.Savings, 650m, now.AddMonths(-4));

        db.Members.AddRange(sarah, daniel, maria);
        await db.SaveChangesAsync(cancellationToken);
    }
}