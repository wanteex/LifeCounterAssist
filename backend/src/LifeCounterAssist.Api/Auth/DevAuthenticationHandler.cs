using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LifeCounterAssist.Api.Auth;

/// <summary>
/// Development-only authentication accepting <c>Bearer dev:{userId}:{displayName}</c>.
/// Lets the app run locally without an Entra tenant. Never enabled outside Development.
/// </summary>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Scheme name.</summary>
    public const string SchemeName = "Dev";

    private const string TokenPrefix = "dev:";

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken();
        if (token is null || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = token[TokenPrefix.Length..].Split(':', 2);
        var userId = Uri.UnescapeDataString(parts[0]);
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 64)
            return Task.FromResult(AuthenticateResult.Fail("Invalid dev token."));

        var name = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : userId;
        Claim[] claims =
        [
            new(ClaimNames.ObjectId, "dev-" + userId),
            new(ClaimNames.Name, name),
            new(ClaimNames.Email, $"{userId}@dev.local"),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    private string? ReadToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();

        // SignalR WebSocket connections pass the token in the query string.
        return Request.Path.StartsWithSegments("/hubs") ? Request.Query["access_token"].ToString() : null;
    }
}
