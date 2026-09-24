using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Messaging;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Events;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Orders.Domain;
using MyWorkplace.Orders.Persistence;

namespace MyWorkplace.Orders.Features;

/// <summary>
/// EN: <c>POST /orders/{id}/place</c> — places a draft: next per-company number, frozen lines, <c>OrderPlaced</c>
///     published through the outbox — all in one transaction (ADR-024).
/// TR: <c>POST /orders/{id}/place</c> — bir taslağı verir: firmaya özel sıradaki numara, dondurulmuş satırlar, outbox üzerinden
///     yayınlanan <c>OrderPlaced</c> — hepsi tek transaction'da (ADR-024).
/// </summary>
public static class PlaceOrder
{
    /// <summary>
    /// EN: How often placing is retried when another placement took the same number first.
    /// TR: Başka bir sipariş verme aynı numarayı önce aldığında kaç kez yeniden deneneceği.
    /// </summary>
    private const int MaxAttempts = 10;

    /// <summary>
    /// EN: Maps the endpoint on the /orders group.
    /// TR: Uç noktayı /orders grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /orders group. TR: /orders grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapPlaceOrder(this IEndpointRouteBuilder group) =>
        group.MapPost("/{id:guid}/place", HandleAsync)
            .WithName("PlaceOrder")
            .RequireAuthorization(Permissions.Orders.Write)
            .WithSummary("EN: Place an order | TR: Siparişi ver")
            .WithDescription(
                "EN: Places a draft: it gets the next order number of your company and can no longer change. Requires " +
                "If-Match, so you place exactly what you last saw (428 / 412). Placing again → 409. " +
                "TR: Bir taslağı verir: firmanızın sıradaki sipariş numarasını alır ve artık değişemez. If-Match gerekir; " +
                "böylece en son gördüğünüzü verirsiniz (428 / 412). Tekrar vermek → 409.");

    /// <summary>
    /// EN: Handles the request. Two placements can take the same number at the same moment; the counter's version makes
    ///     one of them fail, and that one is retried in a fresh scope (new context, new outbox — nothing half-saved is
    ///     carried over).
    /// TR: İsteği işler. İki sipariş verme aynı anda aynı numarayı alabilir; sayacın sürümü birini başarısız kılar ve o, yeni bir
    ///     kapsamda yeniden denenir (yeni context, yeni outbox — yarım kaydedilmiş hiçbir şey taşınmaz).
    /// </summary>
    /// <param name="id">EN: Order id. TR: Sipariş kimliği.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="scopes">EN: Creates one scope per attempt. TR: Her deneme için bir kapsam oluşturur.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404, 409, 412 or 428. TR: 200, 404, 409, 412 veya 428.</returns>
    public static async Task<Results<Ok<OrderResponse>, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        HttpContext http,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (!http.Request.TryReadIfMatch(out var expectedVersion, out var preconditionProblem))
        {
            return preconditionProblem;
        }

        for (var attempt = 1; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var outcome = await TryPlaceAsync(scope.ServiceProvider, id, expectedVersion, cancellationToken);
            if (outcome is { Result: { } result })
            {
                if (outcome.Version is { } version)
                {
                    http.Response.SetETag(version);
                }

                return result;
            }

            if (attempt == MaxAttempts)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Too many orders are being placed at once.",
                    detail: "Try again in a moment.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 25) * attempt), cancellationToken);
        }
    }

    /// <summary>
    /// EN: One attempt. Returns no result when the number was taken meanwhile, so the caller retries.
    /// TR: Tek deneme. Numara bu arada alındıysa sonuç dönmez; çağıran yeniden dener.
    /// </summary>
    /// <param name="services">EN: The attempt's scope. TR: Denemenin kapsamı.</param>
    /// <param name="id">EN: Order id. TR: Sipariş kimliği.</param>
    /// <param name="expectedVersion">EN: Version from If-Match. TR: If-Match'teki sürüm.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The result and new version, or nothing to retry. TR: Sonuç ve yeni sürüm ya da yeniden denemek için boş.</returns>
    private static async Task<(Results<Ok<OrderResponse>, NotFound, ProblemHttpResult>? Result, uint? Version)> TryPlaceAsync(
        IServiceProvider services,
        Guid id,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<OrdersDbContext>();
        var outbox = services.GetRequiredService<IEventOutbox>();
        var time = services.GetRequiredService<TimeProvider>();

        var order = await db.Orders.FindForUpdateAsync(id, cancellationToken);
        if (order is null)
        {
            return (TypedResults.NotFound(), null);
        }

        if (!order.IsDraft)
        {
            return (OrderProblems.AlreadyPlaced(), null);
        }

        if (db.GetVersion(order) != expectedVersion)
        {
            return (ETags.PreconditionFailed(), null);
        }

        var sequence = await db.OrderNumberSequences.AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (sequence is null)
        {
            sequence = new OrderNumberSequence();
            db.OrderNumberSequences.Add(sequence);
        }

        db.ExpectVersion(order, expectedVersion);
        order.Place(sequence.Next(), time.GetUtcNow());
        await outbox.AddAsync(new OrderPlaced
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            Number = order.Number!.Value,
            Lines = [.. order.Lines.OrderBy(l => l.LineNumber).Select(l => new OrderPlacedLine(l.Sku, l.Name, l.Quantity))],
        });

        try
        {
            await outbox.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // EN: The counter (or the order) changed since we read it; the next attempt reads again and decides.
            // TR: Sayaç (veya sipariş) okuduğumuzdan beri değişti; bir sonraki deneme tekrar okur ve karar verir.
            return (null, null);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // EN: Two first placements of a company both created its counter. TR: Firmanın iki ilk siparişi sayacı birlikte oluşturdu.
            return (null, null);
        }

        return (TypedResults.Ok(OrderResponse.From(order)), db.GetVersion(order));
    }
}
