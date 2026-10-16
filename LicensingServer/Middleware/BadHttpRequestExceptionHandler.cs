using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PDV_Adega.LicensingServer.Middleware;

internal sealed class BadHttpRequestExceptionHandler(ILogger<BadHttpRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException) return false;

        logger.LogWarning(exception, "Rejected an invalid HTTP request.");
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = "A solicitação é inválida ou contém JSON malformado.",
            Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}
