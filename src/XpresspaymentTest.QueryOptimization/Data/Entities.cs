namespace XpresspaymentTest.QueryOptimization.Data;

public class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public List<Account> Accounts { get; set; } = new();
}

public class Account
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string Currency { get; set; } = "NGN";
    public decimal Balance { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime OpenedAt { get; set; }

    public Customer Customer { get; set; } = null!;
    public List<Transaction> Transactions { get; set; } = new();
}

public enum TransactionType : byte
{
    Credit = 1,
    Debit = 2
}

public enum TransactionStatus : byte
{
    Pending = 0,
    Successful = 1,
    Failed = 2,
    Reversed = 3
}

public class Transaction
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Account Account { get; set; } = null!;
}
