using LifeCounterAssist.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace LifeCounterAssist.Api.Endpoints;

/// <summary>Maps <see cref="DomainException"/> to RFC 7807 problem responses carrying a stable <c>code</c>.</summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain) return false;

        httpContext.Response.StatusCode = domain switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            ForbiddenException => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Title = domain.Code, Extensions = { ["code"] = domain.Code } },
        });
    }
}
