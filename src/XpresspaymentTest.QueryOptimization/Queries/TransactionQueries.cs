using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.QueryOptimization.Data;

namespace XpresspaymentTest.QueryOptimization.Queries;

/// <summary>
/// The SQL that each of these generates (and a hand written version) is in Sql/Queries.sql.
/// Turn on Microsoft.EntityFrameworkCore.Database.Command logging to see them run.
/// </summary>
public class TransactionQueries
{
    private readonly BankDbContext _db;

    public TransactionQueries(BankDbContext db)
    {
        _db = db;
    }

    // 1. Transaction history with keyset pagination.
    //    Skip(n) makes SQL Server read and throw away n rows, so page 5,000 is much slower than page 1.
    //    Seeking from the last row we returned costs the same on every page and uses IX (AccountId, CreatedAt).
    public async Task<TransactionPage> GetHistoryAsync(int accountId, DateTime? beforeDate, long? beforeId, int pageSize, CancellationToken ct)
    {
        var query = _db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId);

        if (beforeDate is not null && beforeId is not null)
        {
            query = query.Where(t => t.CreatedAt < beforeDate
                || (t.CreatedAt == beforeDate && t.Id < beforeId));
        }

        // fetch one extra row to know if there's a next page without a COUNT(*)
        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(pageSize + 1)
            .Select(t => new TransactionDto(
                t.Id,
                t.Reference,
                t.Amount,
                t.Type == TransactionType.Credit ? "Credit" : "Debit",
                t.Status.ToString(),
                t.Channel,
                t.CreatedAt))
            .ToListAsync(ct);

        if (rows.Count <= pageSize)
            return new TransactionPage(rows, null, null);

        rows.RemoveAt(rows.Count - 1);
        var last = rows[^1];
        return new TransactionPage(rows, last.CreatedAt, last.Id);
    }

    // 2. Customer + accounts + last 5 transactions per account in ONE round trip.
    //    The naive version loads the customer, loops over accounts and queries transactions
    //    for each one (N+1). Projecting lets EF build a single query with OUTER APPLY.
    public Task<CustomerOverviewDto?> GetCustomerOverviewAsync(int customerId, CancellationToken ct)
    {
        return _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new CustomerOverviewDto(
                c.Id,
                c.FirstName + " " + c.LastName,
                c.Email,
                c.State,
                c.Accounts
                    .Select(a => new AccountOverviewDto(
                        a.AccountNumber,
                        a.Currency,
                        a.Balance,
                        a.Transactions
                            .OrderByDescending(t => t.CreatedAt)
                            .Take(5)
                            .Select(t => new RecentTransactionDto(t.Reference, t.Amount, t.Type, t.CreatedAt))
                            .ToList()))
                    .ToList()))
            .FirstOrDefaultAsync(ct);
    }

    // 3. Monthly volume for a year. Grouping and summing happen in SQL, only 24 rows come back.
    //    Uses a date range instead of t.CreatedAt.Year == year so the index can still be used.
    public Task<List<MonthlyVolumeRow>> GetMonthlyVolumeAsync(int year, CancellationToken ct)
    {
        var start = new DateTime(year, 1, 1);
        var end = start.AddYears(1);

        return _db.Transactions
            .AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt < end && t.Status == TransactionStatus.Successful)
            .GroupBy(t => new { t.CreatedAt.Month, t.Type })
            .Select(g => new MonthlyVolumeRow
            {
                Month = g.Key.Month,
                Type = g.Key.Type,
                TransactionCount = g.Count(),
                TotalAmount = g.Sum(t => t.Amount)
            })
            .OrderBy(x => x.Month).ThenBy(x => x.Type)
            .ToListAsync(ct);
    }

    // 3b. Same thing written as raw SQL. Interpolated values become parameters, not string concatenation.
    public Task<List<MonthlyVolumeRow>> GetMonthlyVolumeSqlAsync(int year, CancellationToken ct)
    {
        var start = new DateTime(year, 1, 1);
        var end = start.AddYears(1);

        return _db.Database.SqlQuery<MonthlyVolumeRow>($"""
            SELECT MONTH(t.CreatedAt) AS [Month],
                   t.[Type],
                   COUNT(*)           AS TransactionCount,
                   SUM(t.Amount)      AS TotalAmount
            FROM Transactions t
            WHERE t.CreatedAt >= {start} AND t.CreatedAt < {end}
              AND t.[Status] = {(byte)TransactionStatus.Successful}
            GROUP BY MONTH(t.CreatedAt), t.[Type]
            """)
            .OrderBy(x => x.Month).ThenBy(x => x.Type)
            .ToListAsync(ct);
    }

    // 4. Top spenders in a period. Aggregate first, take the top N, then look up the customer
    //    for just those N rows instead of joining every transaction to Customers.
    public Task<List<TopCustomerDto>> GetTopCustomersAsync(DateTime start, DateTime end, int top, CancellationToken ct)
    {
        return _db.Transactions
            .Where(t => t.CreatedAt >= start && t.CreatedAt < end
                && t.Status == TransactionStatus.Successful
                && t.Type == TransactionType.Debit)
            .GroupBy(t => t.Account.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count(), Total = g.Sum(t => t.Amount) })
            .OrderByDescending(x => x.Total)
            .Take(top)
            // a Join() here doesn't translate after GroupBy + Take, a correlated lookup does (OUTER APPLY)
            .Select(x => new
            {
                x.Count,
                x.Total,
                Customer = _db.Customers.First(c => c.Id == x.CustomerId)
            })
            .Select(x => new TopCustomerDto(
                x.Customer.Id,
                x.Customer.FirstName + " " + x.Customer.LastName,
                x.Customer.Email,
                x.Count,
                x.Total))
            .ToListAsync(ct);
    }

    // 5. Account name enquiry - called constantly before every transfer, so compile it once
    //    and skip the LINQ -> SQL translation on every call.
    private static readonly Func<BankDbContext, string, Task<AccountLookupDto?>> AccountByNumber =
        EF.CompileAsyncQuery((BankDbContext db, string accountNumber) =>
            db.Accounts
                .AsNoTracking()
                .Where(a => a.AccountNumber == accountNumber)
                .Select(a => new AccountLookupDto(
                    a.AccountNumber,
                    a.Currency,
                    a.Balance,
                    a.Customer.FirstName + " " + a.Customer.LastName))
                .FirstOrDefault());

    public Task<AccountLookupDto?> LookupAccountAsync(string accountNumber) => AccountByNumber(_db, accountNumber);

    // 6. When we do need full entities with more than one collection, a single JOIN query
    //    repeats every customer column for every transaction row (cartesian explosion).
    //    AsSplitQuery sends one query per collection instead.
    public Task<List<Customer>> GetCustomersWithActivityAsync(string state, int take, CancellationToken ct)
    {
        return _db.Customers
            .AsNoTracking()
            .Where(c => c.State == state)
            .OrderBy(c => c.Id)
            .Take(take)
            .Include(c => c.Accounts)
                .ThenInclude(a => a.Transactions.OrderByDescending(t => t.CreatedAt).Take(3))
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    // 7. Bulk update without loading anything into memory: one UPDATE statement
    //    instead of SELECT + N tracked entities + N UPDATEs on SaveChanges.
    public Task<int> FailStalePendingAsync(TimeSpan olderThan, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - olderThan;

        return _db.Transactions
            .Where(t => t.Status == TransactionStatus.Pending && t.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TransactionStatus.Failed)
                .SetProperty(t => t.Narration, t => t.Narration + " (timed out)"), ct);
    }
}
