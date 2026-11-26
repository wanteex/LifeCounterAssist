namespace LifeCounterAssist.Api.Domain;

/// <summary>Raised when a request violates a game or business rule.</summary>
/// <param name="code">Stable error code that clients can translate.</param>
public class DomainException(string code) : Exception(code)
{
    /// <summary>Gets the stable error code.</summary>
    public string Code { get; } = code;
}

/// <summary>Raised when the caller is not allowed to perform an action.</summary>
/// <param name="code">Stable error code.</param>
public sealed class ForbiddenException(string code) : DomainException(code);

/// <summary>Raised when a requested resource does not exist.</summary>
/// <param name="code">Stable error code.</param>
public sealed class NotFoundException(string code) : DomainException(code);
