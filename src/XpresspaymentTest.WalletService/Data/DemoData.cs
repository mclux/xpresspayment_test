using Microsoft.EntityFrameworkCore;

namespace XpresspaymentTest.WalletService.Data;

public static class DemoData
{
    // Fixed ids so they can be pasted straight into the PaymentService requests
    public static readonly Guid AdaWallet = Guid.Parse("8f6c2a4e-1b3d-4c5e-9a7b-0d1e2f3a4b5c");
    public static readonly Guid TundeWallet = Guid.Parse("2b7d9e1f-3c4a-4e6b-8d0f-1a2b3c4d5e6f");

    public static async Task EnsureWalletsAsync(WalletDbContext db)
    {
        if (await db.Wallets.AnyAsync())
            return;

        db.Wallets.AddRange(
            new Wallet { Id = AdaWallet, OwnerName = "Ada Obi", Currency = "NGN", Balance = 50_000m },
            new Wallet { Id = TundeWallet, OwnerName = "Tunde Bello", Currency = "NGN", Balance = 1_500m });

        await db.SaveChangesAsync();
    }
}
