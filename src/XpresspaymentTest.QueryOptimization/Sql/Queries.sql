-- SQL versions of the queries in Queries/TransactionQueries.cs (SQL Server).
-- Numbers match the comments in that file.


-- Indexes (created by the EF migration, listed here for reference)
CREATE NONCLUSTERED INDEX IX_Transactions_AccountId_CreatedAt
    ON Transactions (AccountId ASC, CreatedAt DESC)
    INCLUDE (Amount, [Type], [Status]);

CREATE NONCLUSTERED INDEX IX_Transactions_CreatedAt_Status
    ON Transactions (CreatedAt, [Status])
    INCLUDE (Amount, [Type], AccountId);


-- 1. Transaction history, keyset pagination
--    First page: leave out the second predicate.
--    Next pages: pass the CreatedAt/Id of the last row from the previous page.
DECLARE @AccountId INT = 1, @PageSize INT = 20;
DECLARE @BeforeDate DATETIME2 = '2026-10-01', @BeforeId BIGINT = 123456;

SELECT TOP (@PageSize + 1)
       t.Id, t.Reference, t.Amount, t.[Type], t.[Status], t.Channel, t.CreatedAt
FROM Transactions t
WHERE t.AccountId = @AccountId
  AND (t.CreatedAt < @BeforeDate OR (t.CreatedAt = @BeforeDate AND t.Id < @BeforeId))
ORDER BY t.CreatedAt DESC, t.Id DESC;

-- vs. the OFFSET version, which gets slower the deeper you page:
-- SELECT ... ORDER BY t.CreatedAt DESC OFFSET 100000 ROWS FETCH NEXT 20 ROWS ONLY;


-- 2. Customer overview: customer + accounts + last 5 transactions per account, one round trip
DECLARE @CustomerId INT = 1;

SELECT c.Id, c.FirstName + ' ' + c.LastName AS FullName, c.Email, c.[State],
       a.AccountNumber, a.Currency, a.Balance,
       rt.Reference, rt.Amount, rt.[Type], rt.CreatedAt
FROM Customers c
LEFT JOIN Accounts a ON a.CustomerId = c.Id
OUTER APPLY (
    SELECT TOP (5) t.Reference, t.Amount, t.[Type], t.CreatedAt
    FROM Transactions t
    WHERE t.AccountId = a.Id
    ORDER BY t.CreatedAt DESC
) rt
WHERE c.Id = @CustomerId
ORDER BY a.Id, rt.CreatedAt DESC;


-- 3. Monthly volume for a year (also run from C# via Database.SqlQuery, see GetMonthlyVolumeSqlAsync)
--    Range on CreatedAt rather than YEAR(CreatedAt) = 2026 so the predicate stays sargable.
DECLARE @Start DATETIME2 = '2026-01-01', @End DATETIME2 = '2027-01-01';

SELECT MONTH(t.CreatedAt) AS [Month],
       t.[Type],
       COUNT(*)           AS TransactionCount,
       SUM(t.Amount)      AS TotalAmount
FROM Transactions t
WHERE t.CreatedAt >= @Start AND t.CreatedAt < @End
  AND t.[Status] = 1 -- Successful
GROUP BY MONTH(t.CreatedAt), t.[Type]
ORDER BY [Month], t.[Type];


-- 4. Top N spenders in a period. Aggregate in a CTE, then look up only N customers.
DECLARE @From DATETIME2 = DATEADD(DAY, -30, SYSUTCDATETIME()), @To DATETIME2 = SYSUTCDATETIME(), @Top INT = 10;

WITH Totals AS (
    SELECT TOP (@Top)
           a.CustomerId,
           COUNT(*)      AS TransactionCount,
           SUM(t.Amount) AS TotalSpent
    FROM Transactions t
    INNER JOIN Accounts a ON a.Id = t.AccountId
    WHERE t.CreatedAt >= @From AND t.CreatedAt < @To
      AND t.[Status] = 1 -- Successful
      AND t.[Type] = 2   -- Debit
    GROUP BY a.CustomerId
    ORDER BY SUM(t.Amount) DESC
)
SELECT c.Id AS CustomerId, c.FirstName + ' ' + c.LastName AS FullName, c.Email,
       tot.TransactionCount, tot.TotalSpent
FROM Totals tot
INNER JOIN Customers c ON c.Id = tot.CustomerId
ORDER BY tot.TotalSpent DESC;


-- 5. Account name enquiry (compiled query in C#). Seek on the unique AccountNumber index.
DECLARE @AccountNumber CHAR(10) = '1000000005';

SELECT TOP (1) a.AccountNumber, a.Currency, a.Balance, c.FirstName + ' ' + c.LastName AS AccountName
FROM Accounts a
INNER JOIN Customers c ON c.Id = a.CustomerId
WHERE a.AccountNumber = @AccountNumber;


-- 6. Split query: what AsSplitQuery() sends instead of one big JOIN.
--    One statement per collection, so customer columns aren't repeated for every transaction row.
DECLARE @StateName NVARCHAR(50) = N'Lagos', @Take INT = 10;

SELECT TOP (@Take) c.* FROM Customers c WHERE c.[State] = @StateName ORDER BY c.Id;

SELECT a.*
FROM (SELECT TOP (@Take) Id FROM Customers WHERE [State] = @StateName ORDER BY Id) c
INNER JOIN Accounts a ON a.CustomerId = c.Id;

SELECT rt.*
FROM (SELECT TOP (@Take) Id FROM Customers WHERE [State] = @StateName ORDER BY Id) c
INNER JOIN Accounts a ON a.CustomerId = c.Id
CROSS APPLY (
    SELECT TOP (3) t.* FROM Transactions t WHERE t.AccountId = a.Id ORDER BY t.CreatedAt DESC
) rt;


-- 7. Bulk status update, single statement, nothing loaded into the app
DECLARE @Cutoff DATETIME2 = DATEADD(HOUR, -24, SYSUTCDATETIME());

UPDATE Transactions
SET [Status] = 2, -- Failed
    Narration = Narration + N' (timed out)'
WHERE [Status] = 0 -- Pending
  AND CreatedAt < @Cutoff;
