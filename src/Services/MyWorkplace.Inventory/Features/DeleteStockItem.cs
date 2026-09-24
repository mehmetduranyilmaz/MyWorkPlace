using Microsoft.AspNetCore.Http.HttpResults;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Contracts.Identity;
using MyWorkplace.Inventory.Persistence;

namespace MyWorkplace.Inventory.Features;

/// <summary>
/// EN: <c>DELETE /inventory/items/{id}</c> — soft delete, allowed only when the balance is zero.
/// TR: <c>DELETE /inventory/items/{id}</c> — soft delete; sadece bakiye sıfırken izinli.
/// </summary>
public static class DeleteStockItem
{
    /// <summary>
    /// EN: Maps the endpoint on the /inventory/items group.
    /// TR: Uç noktayı /inventory/items grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The items group. TR: Kalemler grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapDeleteStockItem(this IEndpointRouteBuilder group) =>
        group.MapDelete("/{id:guid}", HandleAsync)
            .WithName("DeleteStockItem")
            .RequireAuthorization(Permissions.Inventory.Delete)
            .WithSummary("EN: Delete a stock item | TR: Stok kalemini sil")
            .WithDescription(
                "EN: Deletes an item whose balance is zero (409 otherwise). The record is kept for history and its " +
                "SKU can be used again. " +
                "TR: Bakiyesi sıfır olan bir kalemi siler (değilse 409). Kayıt geçmiş için saklanır ve SKU'su tekrar " +
                "kullanılabilir.");

    /// <summary>
    /// EN: Handles the request.
    /// TR: İsteği işler.
    /// </summary>
    /// <param name="id">EN: Item id. TR: Kalem kimliği.</param>
    /// <param name="db">EN: Inventory database. TR: Inventory veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 204, 404 or 409. TR: 204, 404 veya 409.</returns>
    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var item = await db.StockItems.FindForUpdateAsync(id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        // EN: Deleting stock that physically exists would make the books lie; empty it with movements first.
        // TR: Fiziksel olarak var olan stoğu silmek kayıtları yanıltır; önce hareketlerle sıfırlanmalı.
        if (item.HasStock)
        {
            return StockItemProblems.StillHasStock();
        }

        db.StockItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
