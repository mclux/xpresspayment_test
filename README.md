# Xpress Payment - Backend Assessment

One solution, one project per question. Everything targets .NET 9 and uses SQL Server LocalDB.

| # | Question | Project | Port |
|---|----------|---------|------|
| 1 | Authentication & authorization | `XpresspaymentTest.Auth` | 5101 |
| 2 | EF Core query optimization | `XpresspaymentTest.QueryOptimization` | 5102 |
| 3 | Rate limiting | `XpresspaymentTest.RateLimiting` | 5103 |
| 4 | Background jobs (Hangfire) | `XpresspaymentTest.BackgroundJobs` | 5104 |
| 5 | Microservices (REST) | `XpresspaymentTest.WalletService` + `XpresspaymentTest.PaymentService` | 5105 / 5106 |

## Running

Requirements: .NET 9 SDK, SQL Server LocalDB (comes with Visual Studio).

```bash
dotnet build
dotnet run --project src/XpresspaymentTest.Auth
```

Each project applies its own migrations on startup, so there's nothing to run by hand. Each one has its own database (`XpressAuthDb`, `XpressBankDb`, `XpressJobsDb`, `XpressWalletDb`, `XpressPaymentDb`). To use a full SQL Server instead, change the connection string in that project's `appsettings.json`.

Swagger is at `http://localhost:<port>/swagger`, and every project has a `.http` file with ready-made requests (VS / Rider / VS Code REST Client).

For question 5, start **both** WalletService and PaymentService (in Visual Studio: right-click the solution → *Configure Startup Projects* → Multiple).

---

## 1. Auth (`XpresspaymentTest.Auth`)

ASP.NET Core Identity (EF Core store) issuing JWT bearer tokens.

- `POST /api/auth/register`: new users always get the `Customer` role
- `POST /api/auth/login`: returns a JWT with the user's roles as claims. 5 bad passwords locks the account for 10 minutes.
- `GET /api/profile`: any authenticated user
- `GET /api/profile/wallet`: `Customer` role only
- `GET /api/admin/users`: `BackOffice` policy (Admin **or** Support)
- `POST /api/admin/users/{id}/roles`, `DELETE /api/admin/users/{id}/roles/{role}`, `DELETE /api/admin/users/{id}`: Admin only

Roles (`Admin`, `Support`, `Customer`) and a default admin are seeded on startup:
`admin@xpresstest.local` / `Admin@12345`.

Uses both `[Authorize(Roles = ...)]` and a named policy, since they suit different cases: roles for a single role, a policy for "any of these roles" so the rule lives in one place.

## 2. Query optimization (`XpresspaymentTest.QueryOptimization`)

Customers → Accounts → Transactions. On first run it seeds 5,000 customers and 300,000 transactions (about 10 seconds, using `SqlBulkCopy`). The counts are configurable under `Seed` in appsettings.

The queries are in `Queries/TransactionQueries.cs`, and the SQL version of each one is in `Sql/Queries.sql`. SQL logging is on in Development, so you can see exactly what EF sends.

| Query | Technique | Why |
|---|---|---|
| Transaction history | Keyset pagination, `AsNoTracking`, projection | `OFFSET` gets slower the deeper you page. Seeking from the last row costs the same on every page |
| Customer overview | Single projection with nested `Take(5)` | Removes N+1: one round trip instead of 1 + accounts |
| Monthly volume | `GroupBy` + `Sum` in SQL, date range instead of `.Year` | 24 rows over the wire instead of the whole year. Predicate stays sargable |
| Monthly volume (SQL) | `Database.SqlQuery<T>` with interpolated params | Raw SQL version, still parameterised |
| Top spenders | Aggregate → `Take(N)` → look up customer | Only N customer lookups instead of joining every row |
| Account lookup | `EF.CompileAsyncQuery` | Hot path. Skips LINQ translation on every call |
| Customers by state | `Include` + filtered include + `AsSplitQuery` | Avoids the cartesian explosion of a single JOIN across two collections |
| Fail stale pending | `ExecuteUpdateAsync` | One UPDATE statement, nothing loaded into memory |

Other things that matter here: covering indexes on `(AccountId, CreatedAt DESC)` and `(CreatedAt, Status)`, `AddDbContextPool`, and keeping enums as `tinyint`.

## 3. Rate limiting (`XpresspaymentTest.RateLimiting`)

A custom middleware (`Throttling/`) rather than the built-in `Microsoft.AspNetCore.RateLimiting`. The question asks for the mechanism itself, and writing it shows how it works.

- **Sliding window log** per client. A fixed window lets a client send 2x the limit around the window boundary, and this doesn't.
- Clients are identified by the `X-Api-Key` header (each key has its own limit) and fall back to the IP address when there's no key. An unknown key gets **401**.
- Every response carries `X-RateLimit-Limit`, `X-RateLimit-Remaining` and `X-RateLimit-Reset` (seconds).
- Over the limit, the client gets **429 Too Many Requests** with a `Retry-After` header and a ProblemDetails body.
- A background service evicts idle clients so memory doesn't grow with every IP seen.
- `/swagger` and `/health` are excluded.

Config (`appsettings.json`): 60 second window, 5 requests per IP, keys `basic-3f9a1c` (20) and `premium-8b72de` (100).

Limitation: counters live in memory, so they're per instance. Behind a load balancer they'd move to Redis (a sorted set per client works the same way), and `UseForwardedHeaders` would be needed to get the real client IP.

## 4. Background jobs (`XpresspaymentTest.BackgroundJobs`)

Scenario: **account statements**. Building the statement and emailing it is slow and depends on a mail server, so it shouldn't happen inside the HTTP request.

1. `POST /api/statements` saves a `StatementRequest` and enqueues a Hangfire job, then returns **202 Accepted** immediately.
   - Pass `deliverAt` to schedule it for later instead (`BackgroundJob.Schedule`).
2. `StatementJob` builds a CSV and emails it.
3. `GET /api/statements/{id}` shows the status, attempt count and last error.

Retries: `[AutomaticRetry(Attempts = 4, DelaysInSeconds = [15, 60, 300, 900])]`. The fake email sender fails 40% of the time (`Email:SimulatedFailureRate`), so you can watch the retries in the dashboard. The job is safe to re-run: the file is only generated once, and a request already marked `Sent` is skipped, so a retry won't email the customer twice. After the last attempt the request is marked `Failed` and the job stays in Hangfire's Failed list for manual requeue.

There's also a recurring job, `daily-statement-report`, which emails ops a summary every day at 7am Lagos time. It can be triggered manually with `POST /api/statements/daily-report/trigger`.

Storage is SQL Server (`hangfire` schema in `XpressJobsDb`). Dashboard: `http://localhost:5104/hangfire`.

## 5. Microservices (`WalletService` + `PaymentService`)

Two services, each with its own database, talking over **REST**.

```
client ──POST /api/payments──▶ PaymentService ──POST /api/wallets/{id}/debit──▶ WalletService
                                  (PaymentDb)          X-Api-Key                    (WalletDb)
```

**WalletService** holds balances and a ledger (`WalletEntries`). Two demo wallets are seeded:
- Ada `8f6c2a4e-1b3d-4c5e-9a7b-0d1e2f3a4b5c`: 50,000 NGN
- Tunde `2b7d9e1f-3c4a-4e6b-8d0f-1a2b3c4d5e6f`: 1,500 NGN

**PaymentService** records payments and debits the wallet.

**Authentication between services:** a custom `ApiKey` authentication handler on the wallet. Each calling service has its own key, the key comparison is constant time, and the caller's name is stored on each ledger entry. A missing or wrong key gets **401**.

**Idempotency / data consistency:**
- The client sends a `reference`. PaymentService stores it with a unique index and passes it to the wallet as the debit reference.
- The wallet has a unique index on `(WalletId, Reference)`. Replaying the same debit returns the original entry (200) instead of debiting again, and the same reference with a different amount gets **409**.
- The balance check and update happen in one `UPDATE ... WHERE Balance >= @amount` inside a transaction, so concurrent debits can't overdraw. A DB check constraint backs that up.

**Error handling:**
- The wallet returns ProblemDetails with a machine-readable `code` (`insufficient_funds`, `currency_mismatch`, `wallet_not_found`, `reference_conflict`).
- PaymentService maps wallet responses to three outcomes:
  - success → payment `Successful`, **201**
  - business rejection → payment `Failed`, **422** with the code
  - wallet unreachable / 5xx / timeout → payment stays `Pending`, **503**. The client can resend the same reference and it picks up where it left off.
- The HttpClient uses `Microsoft.Extensions.Http.Resilience`: retries with backoff, a 5s per-attempt timeout, a 20s total timeout and a circuit breaker. Retrying a POST is only safe because of the idempotent reference.
- Unhandled exceptions return ProblemDetails (`UseExceptionHandler`), so stack traces never reach the caller.

**Validation:** DataAnnotations on all request models (`[ApiController]` returns 400 ValidationProblemDetails automatically). The wallet also checks currency and balance rules.

Why REST over gRPC here: it's easy to inspect and test with plain HTTP tools, the payloads are small, and the call rate between these two services doesn't justify the extra tooling. gRPC would be my pick for high-volume internal calls.

---

### Things I'd add with more time
- Integration tests (WebApplicationFactory + Testcontainers SQL Server), especially for the concurrent debit path
- Secrets (JWT key, API keys) in user-secrets / Key Vault instead of appsettings
- Outbox pattern + a reconciliation job for payments stuck in `Pending`
- Redis-backed rate limiter for multiple instances
