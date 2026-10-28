using XpresspaymentTest.QueryOptimization.Data;

namespace XpresspaymentTest.QueryOptimization.Queries;

public record TransactionDto(long Id, string Reference, decimal Amount, string Type, string Status, string Channel, DateTime CreatedAt);

public record TransactionPage(IReadOnlyList<TransactionDto> Items, DateTime? NextBeforeDate, long? NextBeforeId);

public record RecentTransactionDto(string Reference, decimal Amount, TransactionType Type, DateTime CreatedAt);

public record AccountOverviewDto(string AccountNumber, string Currency, decimal Balance, List<RecentTransactionDto> RecentTransactions);

public record CustomerOverviewDto(int Id, string FullName, string Email, string State, List<AccountOverviewDto> Accounts);

public record AccountLookupDto(string AccountNumber, string Currency, decimal Balance, string AccountName);

public record TopCustomerDto(int CustomerId, string FullName, string Email, int TransactionCount, decimal TotalSpent);

// Plain class (not a record) because Database.SqlQuery maps columns onto settable properties
public class MonthlyVolumeRow
{
    public int Month { get; set; }
    public TransactionType Type { get; set; }
    public int TransactionCount { get; set; }
    public decimal TotalAmount { get; set; }
}
