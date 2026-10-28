using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.PaymentService.Clients;
using XpresspaymentTest.PaymentService.Data;
using XpresspaymentTest.PaymentService.Models;

namespace XpresspaymentTest.PaymentService.Services;

public record ProcessResult(Payment? Payment, bool IsReplay, string? ConflictMessage = null);

public class PaymentProcessor
{
    private readonly PaymentDbContext _db;
    private readonly WalletClient _wallet;
    private readonly ILogger<PaymentProcessor> _logger;

    public PaymentProcessor(PaymentDbContext db, WalletClient wallet, ILogger<PaymentProcessor> logger)
    {
        _db = db;
        _wallet = wallet;
        _logger = logger;
    }

    public async Task<ProcessResult> ProcessAsync(CreatePaymentRequest request, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Reference == request.Reference, ct);
        var isReplay = payment is not null;

        if (payment is not null)
        {
            if (payment.WalletId != request.WalletId || payment.Amount != request.Amount)
                return new ProcessResult(null, true, $"Reference '{request.Reference}' was already used for a different payment.");

            // Already settled one way or the other - just hand back what happened
            if (payment.Status != PaymentStatus.Pending)
                return new ProcessResult(payment, true);

            // Still pending means the last attempt couldn't reach the wallet. Try again;
            // the wallet dedupes on reference so this can't double debit.
            _logger.LogInformation("Retrying pending payment {Reference}", payment.Reference);
        }
        else
        {
            payment = new Payment
            {
                Reference = request.Reference,
                WalletId = request.WalletId!.Value,
                Amount = request.Amount,
                Currency = request.Currency,
                MerchantName = request.MerchantName,
                Description = request.Description
            };
            _db.Payments.Add(payment);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Two identical requests at the same time - let the other one do the work
                return new ProcessResult(null, true, $"A payment with reference '{request.Reference}' is already being processed.");
            }
        }

        var debit = await _wallet.DebitAsync(payment.WalletId,
            new WalletDebitRequest(payment.Amount, payment.Currency, payment.Reference, $"Payment to {payment.MerchantName}"),
            ct);

        switch (debit.Outcome)
        {
            case DebitOutcome.Success:
                payment.Status = PaymentStatus.Successful;
                payment.WalletEntryId = debit.Entry!.EntryId;
                payment.FailureCode = null;
                payment.FailureReason = null;
                payment.CompletedAt = DateTime.UtcNow;
                break;

            case DebitOutcome.Rejected:
                payment.Status = PaymentStatus.Failed;
                payment.FailureCode = debit.ErrorCode;
                payment.FailureReason = debit.ErrorMessage;
                payment.CompletedAt = DateTime.UtcNow;
                break;

            case DebitOutcome.Unavailable:
                // leave it Pending so the client (or a reconciliation job) can retry
                payment.FailureCode = debit.ErrorCode;
                payment.FailureReason = debit.ErrorMessage;
                break;
        }

        // CancellationToken.None on purpose: the money has moved (or not), we must record it
        // even if the HTTP caller has gone away.
        await _db.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Payment {Reference} -> {Status}", payment.Reference, payment.Status);
        return new ProcessResult(payment, isReplay);
    }
}
