namespace XpresspaymentTest.BackgroundJobs.Data;

public enum StatementStatus
{
    Queued,
    Scheduled,
    Processing,
    Sent,
    Failed
}

public class StatementRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public StatementStatus Status { get; set; } = StatementStatus.Queued;
    public string? HangfireJobId { get; set; }
    public int Attempts { get; set; }
    public string? FilePath { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
