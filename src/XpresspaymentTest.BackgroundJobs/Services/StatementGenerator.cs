using System.Globalization;
using System.Text;

namespace XpresspaymentTest.BackgroundJobs.Services;

public class StatementGenerator
{
    private readonly string _outputFolder;

    public StatementGenerator(IConfiguration config, IWebHostEnvironment env)
    {
        _outputFolder = Path.Combine(env.ContentRootPath, config["Statements:OutputFolder"] ?? "statements");
        Directory.CreateDirectory(_outputFolder);
    }

    /// <summary>
    /// Builds a CSV statement and returns the file path. In a real system the lines would
    /// come from core banking; here they're generated from the account number so the same
    /// request always produces the same file.
    /// </summary>
    public async Task<string> GenerateAsync(Guid requestId, string accountNumber, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var random = new Random(accountNumber.GetHashCode());
        var balance = 150_000m;
        var narrations = new[] { "POS Purchase", "Transfer to", "Airtime Topup", "Salary", "Transfer from", "Bill Payment" };

        var csv = new StringBuilder();
        csv.AppendLine("Date,Narration,Debit,Credit,Balance");

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var txCount = random.Next(0, 4);
            for (var i = 0; i < txCount; i++)
            {
                var narration = narrations[random.Next(narrations.Length)];
                var isCredit = narration is "Salary" or "Transfer from";
                var amount = Math.Round((decimal)random.NextDouble() * (isCredit ? 80_000 : 15_000), 2);

                balance += isCredit ? amount : -amount;

                csv.AppendLine(string.Join(",",
                    day.ToString("yyyy-MM-dd"),
                    narration,
                    isCredit ? "" : amount.ToString("F2", CultureInfo.InvariantCulture),
                    isCredit ? amount.ToString("F2", CultureInfo.InvariantCulture) : "",
                    balance.ToString("F2", CultureInfo.InvariantCulture)));
            }
        }

        var path = Path.Combine(_outputFolder, $"statement-{accountNumber}-{requestId:N}.csv");
        await File.WriteAllTextAsync(path, csv.ToString(), ct);
        return path;
    }
}
