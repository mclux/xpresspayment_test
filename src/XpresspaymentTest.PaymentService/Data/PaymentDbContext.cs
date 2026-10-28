using Microsoft.EntityFrameworkCore;

namespace XpresspaymentTest.PaymentService.Data;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(e =>
        {
            e.Property(x => x.Reference).HasMaxLength(64);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.MerchantName).HasMaxLength(150);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FailureCode).HasMaxLength(50);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.WalletId, x.CreatedAt });
        });
    }
}
