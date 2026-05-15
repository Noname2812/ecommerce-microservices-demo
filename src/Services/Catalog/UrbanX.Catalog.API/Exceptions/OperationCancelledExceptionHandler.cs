using Microsoft.AspNetCore.Diagnostics;

namespace UrbanX.Catalog.API.Exceptions;

internal sealed class OperationCancelledExceptionHandler(
    ILogger<OperationCancelledExceptionHandler> logger)
    : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not OperationCanceledException)
            return ValueTask.FromResult(false);

        logger.LogInformation("Request was cancelled by the client.");

        // 499 Client Closed Request — client disconnected before server could respond.
        // Not an RFC status code but widely used (nginx). No body is written because
        // the connection is already gone.
        httpContext.Response.StatusCode = 499;
        return ValueTask.FromResult(true);
    }
}
