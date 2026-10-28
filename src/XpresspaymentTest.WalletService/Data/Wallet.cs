namespace XpresspaymentTest.WalletService.Data;

public class Wallet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerName { get; set; } = string.Empty;
    public string Currency { get; set; } = "NGN";
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum EntryType
{
    Credit,
    Debit
}

/// <summary>
/// Ledger line. Every balance change has one of these, and (WalletId, Reference) is unique
/// so the same request can be replayed without moving money twice.
/// </summary>
public class WalletEntry
{
    public long Id { get; set; }
    public Guid WalletId { get; set; }
    public EntryType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string? Narration { get; set; }
    public string PostedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
