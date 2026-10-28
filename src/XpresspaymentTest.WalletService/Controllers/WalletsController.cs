using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.WalletService.Data;
using XpresspaymentTest.WalletService.Models;
using XpresspaymentTest.WalletService.Services;

namespace XpresspaymentTest.WalletService.Controllers;

[ApiController]
[Authorize]
[Route("api/wallets")]
public class WalletsController : ControllerBase
{
    private readonly WalletDbContext _db;
    private readonly Ledger _ledger;

    public WalletsController(WalletDbContext db, Ledger ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateWalletRequest request, CancellationToken ct)
    {
        var wallet = new Wallet
        {
            OwnerName = request.OwnerName,
            Currency = request.Currency,
            Balance = request.OpeningBalance
        };

        _db.Wallets.Add(wallet);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = wallet.Id }, ToResponse(wallet));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var wallet = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        return wallet is null ? WalletNotFound(id) : Ok(ToResponse(wallet));
    }

    [HttpPost("{id:guid}/debit")]
    public Task<IActionResult> Debit(Guid id, PostEntryRequest request, CancellationToken ct) =>
        Post(id, EntryType.Debit, request, ct);

    [HttpPost("{id:guid}/credit")]
    public Task<IActionResult> Credit(Guid id, PostEntryRequest request, CancellationToken ct) =>
        Post(id, EntryType.Credit, request, ct);

    private async Task<IActionResult> Post(Guid id, EntryType type, PostEntryRequest request, CancellationToken ct)
    {
        var result = await _ledger.PostAsync(id, type, request, User.Identity!.Name!, ct);

        if (result.Error is { } error)
        {
            return error switch
            {
                LedgerError.WalletNotFound => WalletNotFound(id),
                LedgerError.InsufficientFunds => Error(StatusCodes.Status422UnprocessableEntity, "insufficient_funds",
                    "Insufficient funds", "The wallet balance is too low for this debit."),
                LedgerError.CurrencyMismatch => Error(StatusCodes.Status422UnprocessableEntity, "currency_mismatch",
                    "Currency mismatch", $"Wallet {id} does not hold {request.Currency}."),
                LedgerError.ReferenceConflict => Error(StatusCodes.Status409Conflict, "reference_conflict",
                    "Reference already used", $"Reference '{request.Reference}' was already used for a different amount or entry type."),
                _ => throw new ArgumentOutOfRangeException(nameof(error))
            };
        }

        var entry = result.Entry!;
        var body = new EntryResponse(entry.Id, entry.WalletId, entry.Type.ToString(), entry.Amount, entry.BalanceAfter, entry.Reference, entry.CreatedAt);

        // 200 for a replay so the caller can tell nothing new happened, 201 for a fresh posting
        return result.IsReplay ? Ok(body) : StatusCode(StatusCodes.Status201Created, body);
    }

    private ObjectResult WalletNotFound(Guid id) =>
        Error(StatusCodes.Status404NotFound, "wallet_not_found", "Wallet not found", $"No wallet with id {id}.");

    // ProblemDetails with a stable "code" so callers don't have to parse the title text
    private ObjectResult Error(int status, string code, string title, string detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }

    private static WalletResponse ToResponse(Wallet w) => new(w.Id, w.OwnerName, w.Currency, w.Balance, w.UpdatedAt);
}
