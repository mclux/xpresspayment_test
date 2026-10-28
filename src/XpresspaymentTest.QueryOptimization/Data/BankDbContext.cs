using Microsoft.EntityFrameworkCore;

namespace XpresspaymentTest.QueryOptimization.Data;

public class BankDbContext : DbContext
{
    public BankDbContext(DbContextOptions<BankDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.State).HasMaxLength(50);
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Account>(e =>
        {
            e.Property(x => x.AccountNumber).HasMaxLength(10).IsFixedLength();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Balance).HasPrecision(18, 2);
            e.HasIndex(x => x.AccountNumber).IsUnique();
            e.HasIndex(x => x.CustomerId);
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.Property(x => x.Reference).HasMaxLength(30);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Channel).HasMaxLength(20);
            e.Property(x => x.Narration).HasMaxLength(200);
            e.HasIndex(x => x.Reference).IsUnique();

            // Almost every query filters by account and date range, newest first.
            // Including the small columns means the history query never touches the clustered index.
            e.HasIndex(x => new { x.AccountId, x.CreatedAt })
                .IsDescending(false, true)
                .IncludeProperties(x => new { x.Amount, x.Type, x.Status });

            // For the date-range reports across all accounts
            e.HasIndex(x => new { x.CreatedAt, x.Status })
                .IncludeProperties(x => new { x.Amount, x.Type, x.AccountId });
        });
    }
}
