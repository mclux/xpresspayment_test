using Microsoft.AspNetCore.Mvc;
using XpresspaymentTest.QueryOptimization.Queries;

namespace XpresspaymentTest.QueryOptimization.Controllers;

[ApiController]
[Route("api")]
public class ReportsController : ControllerBase
{
    private readonly TransactionQueries _queries;

    public ReportsController(TransactionQueries queries)
    {
        _queries = queries;
    }

    [HttpGet("accounts/{accountId:int}/transactions")]
    public async Task<IActionResult> History(int accountId, DateTime? beforeDate, long? beforeId, int pageSize = 20, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(await _queries.GetHistoryAsync(accountId, beforeDate, beforeId, pageSize, ct));
    }

    [HttpGet("accounts/lookup/{accountNumber}")]
    public async Task<IActionResult> Lookup(string accountNumber)
    {
        var account = await _queries.LookupAccountAsync(accountNumber);
        return account is null ? NotFound() : Ok(account);
    }

    [HttpGet("customers/{customerId:int}/overview")]
    public async Task<IActionResult> CustomerOverview(int customerId, CancellationToken ct)
    {
        var overview = await _queries.GetCustomerOverviewAsync(customerId, ct);
        return overview is null ? NotFound() : Ok(overview);
    }

    [HttpGet("customers/by-state/{state}")]
    public async Task<IActionResult> CustomersByState(string state, int take = 10, CancellationToken ct = default)
    {
        var customers = await _queries.GetCustomersWithActivityAsync(state, Math.Clamp(take, 1, 50), ct);

        // map here so we don't serialize the navigation cycles
        return Ok(customers.Select(c => new
        {
            c.Id,
            Name = $"{c.FirstName} {c.LastName}",
            Accounts = c.Accounts.Select(a => new
            {
                a.AccountNumber,
                a.Balance,
                Recent = a.Transactions.Select(t => new { t.Reference, t.Amount, t.CreatedAt })
            })
        }));
    }

    [HttpGet("reports/monthly-volume/{year:int}")]
    public async Task<IActionResult> MonthlyVolume(int year, CancellationToken ct) =>
        Ok(await _queries.GetMonthlyVolumeAsync(year, ct));

    [HttpGet("reports/monthly-volume/{year:int}/sql")]
    public async Task<IActionResult> MonthlyVolumeSql(int year, CancellationToken ct) =>
        Ok(await _queries.GetMonthlyVolumeSqlAsync(year, ct));

    [HttpGet("reports/top-customers")]
    public async Task<IActionResult> TopCustomers(DateTime? from, DateTime? to, int top = 10, CancellationToken ct = default)
    {
        var end = to ?? DateTime.UtcNow;
        var start = from ?? end.AddDays(-30);
        return Ok(await _queries.GetTopCustomersAsync(start, end, Math.Clamp(top, 1, 100), ct));
    }

    [HttpPost("maintenance/fail-stale-pending")]
    public async Task<IActionResult> FailStalePending(int olderThanHours = 24, CancellationToken ct = default)
    {
        var updated = await _queries.FailStalePendingAsync(TimeSpan.FromHours(olderThanHours), ct);
        return Ok(new { updated });
    }
}
