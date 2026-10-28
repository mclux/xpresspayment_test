using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.PaymentService.Data;
using XpresspaymentTest.PaymentService.Models;
using XpresspaymentTest.PaymentService.Services;

namespace XpresspaymentTest.PaymentService.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly PaymentProcessor _processor;
    private readonly PaymentDbContext _db;

    public PaymentsController(PaymentProcessor processor, PaymentDbContext db)
    {
        _processor = processor;
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePaymentRequest request, CancellationToken ct)
    {
        var result = await _processor.ProcessAsync(request, ct);

        if (result.ConflictMessage is not null)
            return Problem(result.ConflictMessage, statusCode: StatusCodes.Status409Conflict, title: "Duplicate reference");

        var payment = result.Payment!;
        var body = PaymentResponse.From(payment);

        return payment.Status switch
        {
            PaymentStatus.Successful when result.IsReplay => Ok(body),
            PaymentStatus.Successful => CreatedAtAction(nameof(Get), new { id = payment.Id }, body),

            // Business rejection (e.g. insufficient funds). The payment exists and is final.
            PaymentStatus.Failed => UnprocessableEntity(body),

            // Couldn't reach the wallet. Nothing is lost - same reference can be sent again.
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, body)
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return payment is null ? NotFound() : Ok(PaymentResponse.From(payment));
    }

    [HttpGet("by-reference/{reference}")]
    public async Task<IActionResult> GetByReference(string reference, CancellationToken ct)
    {
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Reference == reference, ct);
        return payment is null ? NotFound() : Ok(PaymentResponse.From(payment));
    }
}
