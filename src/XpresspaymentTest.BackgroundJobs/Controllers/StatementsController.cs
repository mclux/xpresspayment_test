using System.ComponentModel.DataAnnotations;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.BackgroundJobs.Data;
using XpresspaymentTest.BackgroundJobs.Jobs;

namespace XpresspaymentTest.BackgroundJobs.Controllers;

public class CreateStatementRequest
{
    [Required, RegularExpression(@"^\d{10}$", ErrorMessage = "Account number must be 10 digits (NUBAN)")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    // Optional - send later instead of right away (e.g. at the start of business hours)
    public DateTimeOffset? DeliverAt { get; set; }
}

[ApiController]
[Route("api/statements")]
public class StatementsController : ControllerBase
{
    private readonly JobsDbContext _db;
    private readonly IBackgroundJobClient _jobs;

    public StatementsController(JobsDbContext db, IBackgroundJobClient jobs)
    {
        _db = db;
        _jobs = jobs;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateStatementRequest body)
    {
        if (body.To < body.From)
            return BadRequest(new { message = "'to' must be on or after 'from'" });

        if (body.To.DayNumber - body.From.DayNumber > 366)
            return BadRequest(new { message = "Statements are limited to one year" });

        var request = new StatementRequest
        {
            AccountNumber = body.AccountNumber,
            Email = body.Email,
            From = body.From,
            To = body.To
        };

        _db.StatementRequests.Add(request);
        await _db.SaveChangesAsync();

        // Save first, then enqueue - the job only carries the id and reads everything else from the DB
        if (body.DeliverAt is { } deliverAt && deliverAt > DateTimeOffset.UtcNow)
        {
            request.HangfireJobId = _jobs.Schedule<StatementJob>(
                j => j.GenerateAndSend(request.Id, null, CancellationToken.None), deliverAt);
            request.Status = StatementStatus.Scheduled;
        }
        else
        {
            request.HangfireJobId = _jobs.Enqueue<StatementJob>(
                j => j.GenerateAndSend(request.Id, null, CancellationToken.None));
        }

        await _db.SaveChangesAsync();

        return AcceptedAtAction(nameof(Get), new { id = request.Id }, new
        {
            request.Id,
            Status = request.Status.ToString(),
            request.HangfireJobId
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var request = await _db.StatementRequests
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id,
                r.AccountNumber,
                r.Email,
                r.From,
                r.To,
                Status = r.Status.ToString(),
                r.Attempts,
                r.LastError,
                r.HangfireJobId,
                r.CreatedAt,
                r.CompletedAt
            })
            .FirstOrDefaultAsync();

        return request is null ? NotFound() : Ok(request);
    }

    // Manually kick the daily report instead of waiting for the schedule
    [HttpPost("daily-report/trigger")]
    public IActionResult TriggerDailyReport([FromServices] IRecurringJobManager recurringJobs)
    {
        recurringJobs.Trigger(DailyStatementReportJob.Id);
        return Accepted();
    }
}
