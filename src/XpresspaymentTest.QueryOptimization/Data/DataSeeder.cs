using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace XpresspaymentTest.QueryOptimization.Data;

/// <summary>
/// Fills the database with enough rows that bad queries actually hurt.
/// Customers and accounts go through EF, transactions go through SqlBulkCopy because
/// inserting a few hundred thousand rows via SaveChanges takes forever.
/// </summary>
public class DataSeeder
{
    private static readonly string[] FirstNames = ["Ada", "Chinedu", "Tunde", "Ngozi", "Emeka", "Funke", "Bola", "Ifeoma", "Musa", "Aisha", "Kelechi", "Yemi", "Segun", "Zainab", "Obinna"];
    private static readonly string[] LastNames = ["Okafor", "Adeyemi", "Bello", "Eze", "Ogunleye", "Ibrahim", "Nwosu", "Balogun", "Okonkwo", "Danjuma", "Afolabi", "Uche"];
    private static readonly string[] States = ["Lagos", "Abuja", "Rivers", "Oyo", "Kano", "Enugu", "Anambra", "Kaduna", "Ogun", "Delta"];
    private static readonly string[] Channels = ["POS", "Web", "Mobile", "USSD", "ATM", "Branch"];

    private readonly BankDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(BankDbContext db, IConfiguration config, ILogger<DataSeeder> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await _db.Customers.AnyAsync(ct))
            return;

        var customerCount = _config.GetValue("Seed:Customers", 5_000);
        var transactionCount = _config.GetValue("Seed:Transactions", 300_000);
        var random = new Random(2026);
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("Seeding {Customers} customers and {Transactions} transactions, this takes a minute...",
            customerCount, transactionCount);

        var customers = new List<Customer>(customerCount);
        var accountNo = 1_000_000_000L;

        for (var i = 1; i <= customerCount; i++)
        {
            var first = FirstNames[random.Next(FirstNames.Length)];
            var last = LastNames[random.Next(LastNames.Length)];
            var createdAt = DateTime.UtcNow.AddDays(-random.Next(30, 1500));

            var customer = new Customer
            {
                FirstName = first,
                LastName = last,
                Email = $"{first}.{last}.{i}@example.com".ToLower(),
                Phone = $"080{random.Next(10_000_000, 99_999_999)}",
                State = States[random.Next(States.Length)],
                CreatedAt = createdAt
            };

            // most people have one account, some have a second (usually USD)
            var accounts = random.NextDouble() < 0.3 ? 2 : 1;
            for (var a = 0; a < accounts; a++)
            {
                customer.Accounts.Add(new Account
                {
                    AccountNumber = (accountNo++).ToString(),
                    Currency = a == 0 ? "NGN" : "USD",
                    Balance = Math.Round((decimal)random.NextDouble() * 500_000, 2),
                    OpenedAt = createdAt
                });
            }

            customers.Add(customer);
        }

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        _db.Customers.AddRange(customers);
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        var accountIds = await _db.Accounts.Select(a => a.Id).ToArrayAsync(ct);

        var table = new DataTable();
        table.Columns.Add("AccountId", typeof(int));
        table.Columns.Add("Reference", typeof(string));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("Type", typeof(byte));
        table.Columns.Add("Status", typeof(byte));
        table.Columns.Add("Channel", typeof(string));
        table.Columns.Add("Narration", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTime));

        for (var i = 0; i < transactionCount; i++)
        {
            var isCredit = random.NextDouble() < 0.4;
            var roll = random.Next(100);
            var status = roll switch
            {
                < 90 => TransactionStatus.Successful,
                < 95 => TransactionStatus.Failed,
                < 98 => TransactionStatus.Pending,
                _ => TransactionStatus.Reversed
            };

            table.Rows.Add(
                accountIds[random.Next(accountIds.Length)],
                $"TXN{i:D10}",
                Math.Round((decimal)random.NextDouble() * (isCredit ? 200_000 : 50_000) + 50, 2),
                (byte)(isCredit ? TransactionType.Credit : TransactionType.Debit),
                (byte)status,
                Channels[random.Next(Channels.Length)],
                isCredit ? "Transfer received" : "Payment",
                DateTime.UtcNow.AddMinutes(-random.Next(0, 60 * 24 * 365)));
        }

        var connectionString = _db.Database.GetConnectionString();
        using var bulk = new SqlBulkCopy(connectionString, SqlBulkCopyOptions.TableLock)
        {
            DestinationTableName = "Transactions",
            BatchSize = 10_000,
            BulkCopyTimeout = 300
        };
        foreach (DataColumn col in table.Columns)
            bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        await bulk.WriteToServerAsync(table, ct);

        _logger.LogInformation("Seeding finished in {Elapsed}", sw.Elapsed);
    }
}
