using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.WalletService.Data;
using XpresspaymentTest.WalletService.Models;

namespace XpresspaymentTest.WalletService.Services;

public enum LedgerError
{
    WalletNotFound,
    CurrencyMismatch,
    InsufficientFunds,
    ReferenceConflict
}

public record LedgerResult(WalletEntry? Entry, LedgerError? Error = null, bool IsReplay = false)
{
    public static LedgerResult Fail(LedgerError error) => new(null, error);
}

public class Ledger
{
    private readonly WalletDbContext _db;
    private readonly ILogger<Ledger> _logger;

    public Ledger(WalletDbContext db, ILogger<Ledger> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<LedgerResult> PostAsync(Guid walletId, EntryType type, PostEntryRequest request, string postedBy, CancellationToken ct)
    {
        var existing = await FindByReference(walletId, request.Reference, ct);
        if (existing is not null)
            return Replay(existing, type, request);

        var wallet = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == walletId, ct);
        if (wallet is null)
            return LedgerResult.Fail(LedgerError.WalletNotFound);

        if (!string.Equals(wallet.Currency, request.Currency, StringComparison.OrdinalIgnoreCase))
            return LedgerResult.Fail(LedgerError.CurrencyMismatch);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Check and update in the same statement so two concurrent debits can't both pass
        // a balance check that was read before either of them wrote.
        var walletQuery = _db.Wallets.Where(w => w.Id == walletId);
        if (type == EntryType.Debit)
            walletQuery = walletQuery.Where(w => w.Balance >= request.Amount);

        var delta = type == EntryType.Debit ? -request.Amount : request.Amount;
        var now = DateTime.UtcNow;

        var updated = await walletQuery.ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Balance, w => w.Balance + delta)
            .SetProperty(w => w.UpdatedAt, now), ct);

        if (updated == 0)
            return LedgerResult.Fail(LedgerError.InsufficientFunds);

        var balanceAfter = await _db.Wallets
            .Where(w => w.Id == walletId)
            .Select(w => w.Balance)
            .SingleAsync(ct);

        var entry = new WalletEntry
        {
            WalletId = walletId,
            Type = type,
            Amount = request.Amount,
            BalanceAfter = balanceAfter,
            Reference = request.Reference,
            Narration = request.Narration,
            PostedBy = postedBy,
            CreatedAt = now
        };
        _db.Entries.Add(entry);

        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Same reference raced in from a parallel request. Undo our balance change
            // and hand back whatever the winner posted.
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            _logger.LogInformation("Duplicate reference {Reference} on wallet {WalletId}, returning original entry",
                request.Reference, walletId);

            var winner = await FindByReference(walletId, request.Reference, ct);
            return Replay(winner!, type, request);
        }

        _logger.LogInformation("{Type} of {Amount} {Currency} on wallet {WalletId} by {PostedBy}, ref {Reference}",
            type, request.Amount, wallet.Currency, walletId, postedBy, request.Reference);

        return new LedgerResult(entry);
    }

    private Task<WalletEntry?> FindByReference(Guid walletId, string reference, CancellationToken ct) =>
        _db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.WalletId == walletId && e.Reference == reference, ct);

    private static LedgerResult Replay(WalletEntry existing, EntryType type, PostEntryRequest request)
    {
        // same reference but different details is a client bug, not a retry
        if (existing.Type != type || existing.Amount != request.Amount)
            return LedgerResult.Fail(LedgerError.ReferenceConflict);

        return new LedgerResult(existing, IsReplay: true);
    }
}
