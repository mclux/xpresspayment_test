using Microsoft.EntityFrameworkCore;

namespace XpresspaymentTest.WalletService.Data;

public class WalletDbContext : DbContext
{
    public WalletDbContext(DbContextOptions<WalletDbContext> options) : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletEntry> Entries => Set<WalletEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>(e =>
        {
            e.Property(x => x.OwnerName).HasMaxLength(150);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Balance).HasPrecision(18, 2);
            e.ToTable(t => t.HasCheckConstraint("CK_Wallets_Balance", "[Balance] >= 0"));
        });

        modelBuilder.Entity<WalletEntry>(e =>
        {
            e.ToTable("WalletEntries");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.BalanceAfter).HasPrecision(18, 2);
            e.Property(x => x.Reference).HasMaxLength(64);
            e.Property(x => x.Narration).HasMaxLength(200);
            e.Property(x => x.PostedBy).HasMaxLength(50);
            e.HasIndex(x => new { x.WalletId, x.Reference }).IsUnique();
            e.HasOne<Wallet>().WithMany().HasForeignKey(x => x.WalletId);
        });
    }
}
