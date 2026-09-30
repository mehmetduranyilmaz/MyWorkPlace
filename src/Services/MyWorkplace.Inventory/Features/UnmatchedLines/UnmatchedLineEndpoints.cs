using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Domain;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features.UnmatchedLines;

/// <summary>
/// EN: The review list of unmatched order lines and the company's ignored SKUs (ADR-020, T-042).
/// TR: Eşleşmeyen sipariş satırlarının inceleme listesi ve firmanın yok sayılan SKU'ları (ADR-020, T-042).
/// </summary>
public static class UnmatchedLineEndpoints
{
    /// <summary>
    /// EN: Maps the review endpoints on the /inventory group.
    /// TR: İnceleme uç noktalarını /inventory grubunda tanımlar.
    /// </summary>
    /// <param name="inventory">EN: The /inventory group. TR: /inventory grubu.</param>
    /// <returns>EN: The same group. TR: Aynı grup.</returns>
    public static IEndpointRouteBuilder MapUnmatchedLines(this IEndpointRouteBuilder inventory)
    {
        var lines = inventory.MapGroup("/unmatched-lines").WithTags("Unmatched order lines");
        lines.MapGet("", ListAsync)
            .WithName("ListUnmatchedLines")
            .RequireAuthorization(Permissions.Inventory.Read)
            .WithSummary("EN: List unmatched order lines | TR: Eşleşmeyen sipariş satırlarını listele")
            .WithDescription(
                "EN: Order lines whose SKU matched no stock item, newest first; status Open by default (also Resolved, " +
                "Dismissed, OrderCancelled). page ≥ 1, pageSize 1–100. " +
                "TR: SKU'su hiçbir stok kalemiyle eşleşmeyen sipariş satırları, en yeni önce; varsayılan durum Open (ayrıca Resolved, " +
                "Dismissed, OrderCancelled). page ≥ 1, pageSize 1–100.")
            .ProducesValidationProblem();
        lines.MapPost("/{id:guid}/resolve", ResolveAsync)
            .WithName("ResolveUnmatchedLine")
            .RequireAuthorization(Permissions.Inventory.Write)
            .WithSummary("EN: Resolve from a stock item | TR: Bir stok kaleminden çöz")
            .WithDescription(
                "EN: Issues the quantity from the chosen item now, as an issue of that order (cancelling the order returns it). " +
                "An entry that is not open → 409; an unknown item → 404. " +
                "TR: Miktarı seçilen kalemden şimdi, o siparişin bir çıkışı olarak çıkar (siparişi iptal etmek onu geri verir). Açık olmayan " +
                "kayıt → 409; bilinmeyen kalem → 404.")
            .ProducesValidationProblem();
        lines.MapPost("/{id:guid}/dismiss", DismissAsync)
            .WithName("DismissUnmatchedLine")
            .RequireAuthorization(Permissions.Inventory.Write)
            .WithSummary("EN: Dismiss | TR: Yok say")
            .WithDescription(
                "EN: Leaves stock as it is (e.g. a service); with ignoreSku the SKU is not listed again. An entry that is not " +
                "open → 409. " +
                "TR: Stoğu olduğu gibi bırakır (ör. bir hizmet); ignoreSku ile SKU tekrar listelenmez. Açık olmayan kayıt → 409.")
            .ProducesValidationProblem();

        var ignored = inventory.MapGroup("/ignored-skus").WithTags("Unmatched order lines");
        ignored.MapGet("", ListIgnoredAsync)
            .WithName("ListIgnoredSkus")
            .RequireAuthorization(Permissions.Inventory.Read)
            .WithSummary("EN: List ignored SKUs | TR: Yok sayılan SKU'ları listele")
            .WithDescription("EN: SKUs never listed as unmatched. TR: Hiç eşleşmeyen olarak listelenmeyen SKU'lar.");
        ignored.MapDelete("/{sku}", RemoveIgnoredAsync)
            .WithName("RemoveIgnoredSku")
            .RequireAuthorization(Permissions.Inventory.Delete)
            .WithSummary("EN: Stop ignoring a SKU | TR: Bir SKU'yu yok saymayı bırak")
            .WithDescription("EN: Later orders list it again when unmatched. TR: Sonraki siparişler eşleşmezse onu tekrar listeler.");
        return inventory;
    }

    /// <summary>
    /// EN: One page of entries with a status, newest first.
    /// TR: Bir durumdaki kayıtlardan bir sayfa, en yeni önce.
    /// </summary>
    /// <param name="page">EN: Page parameters. TR: Sayfa parametreleri.</param>
    /// <param name="status">EN: Status filter (Open by default). TR: Durum filtresi (varsayılan Open).</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The page. TR: Sayfa.</returns>
    public static async Task<Ok<PagedResult<UnmatchedLineResponse>>> ListAsync(
        [AsParameters] PageQuery page,
        UnmatchedLineStatus? status,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var wanted = status ?? UnmatchedLineStatus.Open;
        var result = await db.UnmatchedOrderLines
            .Where(l => l.Status == wanted)
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id)
            .ToPagedResultAsync(UnmatchedLineResponse.Projection, page, cancellationToken);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// EN: Issues the entry's quantity from an item and marks it resolved — in one transaction. If the order is cancelled
    ///     at the same moment, the cancellation changed the entry's version, so the save fails and nothing is issued.
    /// TR: Kaydın miktarını bir kalemden çıkar ve onu çözülmüş işaretler — tek transaction'da. Sipariş aynı anda iptal edilirse iptal kaydın
    ///     sürümünü değiştirmiştir; kaydetme başarısız olur ve hiçbir şey çıkılmaz.
    /// </summary>
    /// <param name="id">EN: Entry id. TR: Kayıt kimliği.</param>
    /// <param name="input">EN: Resolve form. TR: Çözme formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="ledger">EN: Stock ledger. TR: Stok defteri.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404 or 409. TR: 200, 404 veya 409.</returns>
    public static async Task<Results<Ok<UnmatchedLineResponse>, NotFound, ProblemHttpResult>> ResolveAsync(
        Guid id,
        ResolveInput input,
        InventoryDbContext db,
        StockLedger ledger,
        CancellationToken cancellationToken)
    {
        var itemId = input.StockItemId!.Value;
        if (!await db.StockItems.AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return UnmatchedLineProblems.StockItemNotFound();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var line = await db.UnmatchedOrderLines.FindForUpdateAsync(id, ct);
                if (line is null)
                {
                    return (Results<Ok<UnmatchedLineResponse>, NotFound, ProblemHttpResult>)TypedResults.NotFound();
                }

                if (line.Status != UnmatchedLineStatus.Open)
                {
                    return UnmatchedLineProblems.NotOpen();
                }

                line.Resolve(itemId);
                await ledger.IssueForOrderAsync(itemId, line.Quantity, line.OrderId, line.OrderNumber, ct);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return UnmatchedLineProblems.NotOpen();
                }

                await transaction.CommitAsync(ct);
                return TypedResults.Ok(UnmatchedLineResponse.From(line));
            },
            cancellationToken);
    }

    /// <summary>
    /// EN: Dismisses an entry, and optionally ignores its SKU from now on.
    /// TR: Bir kaydı yok sayar ve isteğe bağlı olarak SKU'sunu bundan sonra yok sayar.
    /// </summary>
    /// <param name="id">EN: Entry id. TR: Kayıt kimliği.</param>
    /// <param name="input">EN: Dismiss form. TR: Yok sayma formu.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 404 or 409. TR: 200, 404 veya 409.</returns>
    public static async Task<Results<Ok<UnmatchedLineResponse>, NotFound, ProblemHttpResult>> DismissAsync(
        Guid id,
        DismissInput input,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var line = await db.UnmatchedOrderLines.FindForUpdateAsync(id, cancellationToken);
        if (line is null)
        {
            return TypedResults.NotFound();
        }

        if (line.Status != UnmatchedLineStatus.Open)
        {
            return UnmatchedLineProblems.NotOpen();
        }

        line.Dismiss(input.Note);
        if (input.IgnoreSku && !await db.IgnoredSkus.AnyAsync(s => s.NormalizedSku == line.NormalizedSku, cancellationToken))
        {
            db.IgnoredSkus.Add(new IgnoredSku { Sku = line.Sku, NormalizedSku = line.NormalizedSku });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UnmatchedLineProblems.NotOpen();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // EN: The SKU was ignored meanwhile by another dismissal: dismiss this entry alone.
            // TR: SKU bu arada başka bir yok saymayla yok sayıldı: sadece bu kaydı yok say.
            db.ChangeTracker.Entries<IgnoredSku>().ToList().ForEach(e => e.State = EntityState.Detached);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(UnmatchedLineResponse.From(line));
    }

    /// <summary>
    /// EN: The company's ignored SKUs, by SKU.
    /// TR: Firmanın yok sayılan SKU'ları, SKU'ya göre.
    /// </summary>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The list. TR: Liste.</returns>
    public static async Task<Ok<List<IgnoredSkuResponse>>> ListIgnoredAsync(InventoryDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await db.IgnoredSkus
            .OrderBy(s => s.NormalizedSku)
            .Select(s => new IgnoredSkuResponse(s.Sku, s.CreatedAt))
            .ToListAsync(cancellationToken));

    /// <summary>
    /// EN: Stops ignoring a SKU.
    /// TR: Bir SKU'yu yok saymayı bırakır.
    /// </summary>
    /// <param name="sku">EN: The SKU, any case. TR: SKU, harf büyüklüğü fark etmez.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 204 or 404. TR: 204 veya 404.</returns>
    public static async Task<Results<NoContent, NotFound>> RemoveIgnoredAsync(
        string sku, InventoryDbContext db, CancellationToken cancellationToken)
    {
        var normalized = StockItem.NormalizeSku(sku);
        var deleted = await db.IgnoredSkus.Where(s => s.NormalizedSku == normalized).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}
