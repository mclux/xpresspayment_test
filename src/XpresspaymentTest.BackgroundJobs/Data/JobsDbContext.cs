using Microsoft.EntityFrameworkCore;

namespace XpresspaymentTest.BackgroundJobs.Data;

public class JobsDbContext : DbContext
{
    public JobsDbContext(DbContextOptions<JobsDbContext> options) : base(options)
    {
    }

    public DbSet<StatementRequest> StatementRequests => Set<StatementRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StatementRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountNumber).HasMaxLength(10).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.HangfireJobId).HasMaxLength(50);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });
    }
}
