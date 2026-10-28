using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace XpresspaymentTest.PaymentService.Clients;

public class WalletServiceOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public record WalletDebitRequest(decimal Amount, string Currency, string Reference, string? Narration);

public record WalletEntryResponse(long EntryId, Guid WalletId, string Type, decimal Amount, decimal BalanceAfter, string Reference, DateTime CreatedAt);

public enum DebitOutcome
{
    Success,
    Rejected,       // wallet service said no - insufficient funds, wrong currency etc. Final.
    Unavailable     // we don't know what happened - safe to retry with the same reference
}

public record DebitResult(DebitOutcome Outcome, WalletEntryResponse? Entry = null, string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>
/// Typed HttpClient for the wallet service. Retries/timeouts/circuit breaker come from
/// the resilience handler registered in Program.cs - this class just turns responses into results.
/// </summary>
public class WalletClient
{
    private readonly HttpClient _http;
    private readonly ILogger<WalletClient> _logger;

    public WalletClient(HttpClient http, ILogger<WalletClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<DebitResult> DebitAsync(Guid walletId, WalletDebitRequest request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync($"api/wallets/{walletId}/debit", request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogError(ex, "Wallet service unreachable while debiting {WalletId} ref {Reference}", walletId, request.Reference);
            return new DebitResult(DebitOutcome.Unavailable, ErrorCode: "wallet_service_unavailable",
                ErrorMessage: "Could not reach the wallet service");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var entry = await response.Content.ReadFromJsonAsync<WalletEntryResponse>(ct);
                return new DebitResult(DebitOutcome.Success, entry);
            }

            // Our own key being rejected is a config problem on our side, not the customer's fault
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogCritical("Wallet service rejected our API key ({Status})", (int)response.StatusCode);
                return new DebitResult(DebitOutcome.Unavailable, ErrorCode: "wallet_auth_failed",
                    ErrorMessage: "Payment service is not authorised to call the wallet service");
            }

            var problem = await TryReadProblem(response, ct);
            var code = problem?.Extensions.TryGetValue("code", out var c) == true ? c?.ToString() : null;

            if ((int)response.StatusCode >= 500)
            {
                return new DebitResult(DebitOutcome.Unavailable, ErrorCode: code ?? "wallet_service_error",
                    ErrorMessage: problem?.Detail ?? "Wallet service returned an error");
            }

            return new DebitResult(DebitOutcome.Rejected, ErrorCode: code ?? $"wallet_{(int)response.StatusCode}",
                ErrorMessage: problem?.Detail ?? problem?.Title ?? response.ReasonPhrase);
        }
    }

    private static async Task<ProblemDetails?> TryReadProblem(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemDetails>(ct);
        }
        catch
        {
            // body wasn't JSON (e.g. a proxy error page) - the status code is enough
            return null;
        }
    }
}
