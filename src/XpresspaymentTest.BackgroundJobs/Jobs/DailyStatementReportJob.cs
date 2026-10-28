using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XpresspaymentTest.BackgroundJobs.Data;
using XpresspaymentTest.BackgroundJobs.Services;

namespace XpresspaymentTest.BackgroundJobs.Jobs;

/// <summary>
/// Recurring job: every morning ops get a summary of yesterday's statement requests,
/// mainly so they can follow up on the ones that failed.
/// </summary>
public class DailyStatementReportJob
{
    public const string Id = "daily-statement-report";

    private readonly JobsDbContext _db;
    private readonly IEmailSender _email;
    private readonly EmailSettings _settings;

    public DailyStatementReportJob(JobsDbContext db, IEmailSender email, IOptions<EmailSettings> settings)
    {
        _db = db;
        _email = email;
        _settings = settings.Value;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task Run(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        var counts = await _db.StatementRequests
            .AsNoTracking()
            .Where(r => r.CreatedAt >= yesterday && r.CreatedAt < today)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var lines = counts.Count == 0
            ? "No statement requests yesterday."
            : string.Join(Environment.NewLine, counts.Select(c => $"{c.Status}: {c.Count}"));

        await _email.SendAsync(_settings.OpsEmail, $"Statement requests for {yesterday:dd MMM yyyy}", lines, ct: ct);
    }
}
