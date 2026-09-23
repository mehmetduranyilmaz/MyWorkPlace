using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MyWorkplace.BuildingBlocks.Http;

/// <summary>
/// EN: Turns an optimistic-concurrency failure (someone else changed the row first) into a 409 ProblemDetails,
///     so the client knows to reload and retry instead of seeing a 500.
/// TR: İyimser eşzamanlılık hatasını (satırı önce başkası değiştirdi) 409 ProblemDetails'e çevirir;
///     böylece istemci 500 görmek yerine yeniden yükleyip tekrar denemesi gerektiğini anlar.
/// </summary>
/// <param name="problemDetails">EN: Writes problem responses. TR: Problem cevaplarını yazar.</param>
public sealed class ConcurrencyConflictHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The resource was changed by someone else.",
                Detail = "Reload the latest version and try again.",
            },
        });
    }
}
