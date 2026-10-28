using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.BackgroundJobs.Data;
using XpresspaymentTest.BackgroundJobs.Services;

namespace XpresspaymentTest.BackgroundJobs.Jobs;

public class StatementJob
{
    public const int MaxRetries = 4;

    private readonly JobsDbContext _db;
    private readonly StatementGenerator _generator;
    private readonly IEmailSender _email;
    private readonly ILogger<StatementJob> _logger;

    public StatementJob(JobsDbContext db, StatementGenerator generator, IEmailSender email, ILogger<StatementJob> logger)
    {
        _db = db;
        _generator = generator;
        _email = email;
        _logger = logger;
    }

    // 4 retries after the first attempt, backing off 15s -> 1m -> 5m -> 15m.
    // After that the job goes to the Failed tab in the dashboard and can be requeued by hand.
    [AutomaticRetry(Attempts = MaxRetries, DelaysInSeconds = [15, 60, 300, 900], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [JobDisplayName("Statement {0}")]
    public async Task GenerateAndSend(Guid requestId, PerformContext? context, CancellationToken ct)
    {
        var request = await _db.StatementRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            _logger.LogWarning("Statement request {RequestId} no longer exists, skipping", requestId);
            return;
        }

        // A retry can land here after the email already went out (e.g. the DB save below failed).
        // Don't spam the customer with the same statement twice.
        if (request.Status == StatementStatus.Sent)
            return;

        var retryCount = context?.GetJobParameter<int>("RetryCount") ?? 0;

        request.Status = StatementStatus.Processing;
        request.Attempts = retryCount + 1;
        await _db.SaveChangesAsync(ct);

        try
        {
            // only generate the file once, retries reuse it
            if (request.FilePath is null || !File.Exists(request.FilePath))
            {
                request.FilePath = await _generator.GenerateAsync(request.Id, request.AccountNumber, request.From, request.To, ct);
                await _db.SaveChangesAsync(ct);
            }

            await _email.SendAsync(
                request.Email,
                $"Your account statement ({request.From:dd MMM yyyy} - {request.To:dd MMM yyyy})",
                $"Hi, please find attached the statement for account {request.AccountNumber}.",
                request.FilePath,
                ct);

            request.Status = StatementStatus.Sent;
            request.CompletedAt = DateTime.UtcNow;
            request.LastError = null;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var finalAttempt = retryCount >= MaxRetries;

            _logger.LogWarning(ex, "Statement {RequestId} attempt {Attempt} failed{Final}",
                requestId, request.Attempts, finalAttempt ? " - giving up" : ", will retry");

            request.Status = finalAttempt ? StatementStatus.Failed : StatementStatus.Queued;
            request.LastError = ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);

            // rethrow so Hangfire records the failure and schedules the retry
            throw;
        }
    }
}
