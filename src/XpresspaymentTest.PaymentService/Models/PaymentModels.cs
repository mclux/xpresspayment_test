using System.ComponentModel.DataAnnotations;
using XpresspaymentTest.PaymentService.Data;

namespace XpresspaymentTest.PaymentService.Models;

public class CreatePaymentRequest
{
    [Required, MinLength(6), MaxLength(64)]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Reference can only contain letters, numbers, - and _")]
    public string Reference { get; set; } = string.Empty;

    [Required]
    public Guid? WalletId { get; set; }

    [Range(0.01, 5_000_000, ErrorMessage = "Amount must be between 0.01 and 5,000,000")]
    public decimal Amount { get; set; }

    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency must be a 3 letter ISO code, e.g. NGN")]
    public string Currency { get; set; } = "NGN";

    [Required, MaxLength(150)]
    public string MerchantName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }
}

public record PaymentResponse(
    Guid Id,
    string Reference,
    Guid WalletId,
    decimal Amount,
    string Currency,
    string MerchantName,
    string Status,
    string? FailureCode,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt)
{
    public static PaymentResponse From(Payment p) => new(
        p.Id, p.Reference, p.WalletId, p.Amount, p.Currency, p.MerchantName,
        p.Status.ToString(), p.FailureCode, p.FailureReason, p.CreatedAt, p.CompletedAt);
}
