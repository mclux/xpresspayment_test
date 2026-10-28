using System.ComponentModel.DataAnnotations;

namespace XpresspaymentTest.WalletService.Models;

public class CreateWalletRequest
{
    [Required, MaxLength(150)]
    public string OwnerName { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency must be a 3 letter ISO code, e.g. NGN")]
    public string Currency { get; set; } = "NGN";

    [Range(0, 100_000_000)]
    public decimal OpeningBalance { get; set; }
}

public class PostEntryRequest
{
    [Range(0.01, 10_000_000, ErrorMessage = "Amount must be between 0.01 and 10,000,000")]
    public decimal Amount { get; set; }

    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency must be a 3 letter ISO code, e.g. NGN")]
    public string Currency { get; set; } = string.Empty;

    // Caller's unique reference for this movement. Sending the same one again is safe.
    [Required, MinLength(6), MaxLength(64)]
    public string Reference { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Narration { get; set; }
}

public record WalletResponse(Guid Id, string OwnerName, string Currency, decimal Balance, DateTime UpdatedAt);

public record EntryResponse(long EntryId, Guid WalletId, string Type, decimal Amount, decimal BalanceAfter, string Reference, DateTime CreatedAt);
