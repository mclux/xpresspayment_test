using Microsoft.AspNetCore.Mvc;

namespace XpresspaymentTest.RateLimiting.Controllers;

[ApiController]
[Route("api/exchange-rates")]
public class ExchangeRatesController : ControllerBase
{
    // Hardcoded - the point of this project is the throttling, not the data
    private static readonly Dictionary<string, decimal> NairaRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1535.50m,
        ["GBP"] = 2048.20m,
        ["EUR"] = 1779.85m,
        ["GHS"] = 98.40m,
        ["KES"] = 11.90m
    };

    [HttpGet]
    public IActionResult GetAll() => Ok(new
    {
        Base = "NGN",
        AsAt = DateTime.UtcNow,
        Rates = NairaRates
    });

    [HttpGet("{currency}")]
    public IActionResult Get(string currency)
    {
        if (!NairaRates.TryGetValue(currency, out var rate))
            return NotFound(new { message = $"No rate for {currency.ToUpper()}" });

        return Ok(new { Base = "NGN", Currency = currency.ToUpper(), Rate = rate, AsAt = DateTime.UtcNow });
    }
}
