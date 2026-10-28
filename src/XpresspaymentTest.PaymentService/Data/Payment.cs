namespace XpresspaymentTest.PaymentService.Data;

public enum PaymentStatus
{
    Pending,
    Successful,
    Failed
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Supplied by the client. Also used as the wallet debit reference, which is what
    // makes retrying a payment safe end to end.
    public string Reference { get; set; } = string.Empty;

    public Guid WalletId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NGN";
    public string MerchantName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public long? WalletEntryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
