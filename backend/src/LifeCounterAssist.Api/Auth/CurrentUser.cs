using System.Security.Claims;
using LifeCounterAssist.Api.Domain;

namespace LifeCounterAssist.Api.Auth;

/// <summary>Claim types used by Entra External ID tokens (inbound claim mapping disabled).</summary>
public static class ClaimNames
{
    /// <summary>Stable user object identifier.</summary>
    public const string ObjectId = "oid";

    /// <summary>Subject.</summary>
    public const string Subject = "sub";

    /// <summary>Display name.</summary>
    public const string Name = "name";

    /// <summary>Email.</summary>
    public const string Email = "email";

    /// <summary>Preferred user name (often the email).</summary>
    public const string PreferredUserName = "preferred_username";
}

/// <summary>Authenticated caller identity.</summary>
/// <param name="Id">Stable user identifier.</param>
/// <param name="DisplayName">Name from the identity provider.</param>
/// <param name="Email">Email, when provided.</param>
public sealed record CurrentUser(string Id, string DisplayName, string? Email);

/// <summary>Helpers to read the caller identity from claims.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Builds the <see cref="CurrentUser"/> from claims.</summary>
    /// <param name="principal">Authenticated principal.</param>
    /// <returns>The current user.</returns>
    /// <exception cref="ForbiddenException">Thrown when no identifier claim exists.</exception>
    public static CurrentUser ToCurrentUser(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimNames.ObjectId)
            ?? principal.FindFirstValue(ClaimNames.Subject)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new ForbiddenException("missing_user_id");
        var email = principal.FindFirstValue(ClaimNames.Email)
            ?? principal.FindFirstValue(ClaimNames.PreferredUserName);
        var name = principal.FindFirstValue(ClaimNames.Name);
        if (string.IsNullOrWhiteSpace(name) || name == "unknown")
            name = email?.Split('@')[0] ?? "Player";
        return new CurrentUser(id, name, email);
    }
}
