using LifeCounterAssist.Api.Domain;
using Microsoft.AspNetCore.SignalR;

namespace LifeCounterAssist.Api.Hubs;

/// <summary>Converts <see cref="DomainException"/> into <see cref="HubException"/> so clients receive the error code.</summary>
public sealed class DomainExceptionHubFilter : IHubFilter
{
    /// <inheritdoc/>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (DomainException ex)
        {
            throw new HubException(ex.Code);
        }
    }
}
