using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace XpresspaymentTest.WalletService.Auth;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    // client name -> key. In production these would come from a vault, not appsettings.
    public Dictionary<string, string> Clients { get; set; } = new();
}

/// <summary>
/// Service-to-service auth. Each calling service gets its own key so we can tell who
/// posted what (it ends up in WalletEntry.PostedBy) and revoke one without the others.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var provided)
            || string.IsNullOrWhiteSpace(provided))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());

        foreach (var (clientName, key) in Options.Clients)
        {
            // fixed time compare so the key can't be guessed byte by byte from response times
            if (!CryptographicOperations.FixedTimeEquals(providedBytes, Encoding.UTF8.GetBytes(key)))
                continue;

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, clientName), new Claim(ClaimTypes.Role, "service")],
                ApiKeyAuthenticationOptions.Scheme);

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationOptions.Scheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        Logger.LogWarning("Rejected request with unknown API key from {Ip}", Context.Connection.RemoteIpAddress);
        return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
    }
}
