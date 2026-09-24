using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.BuildingBlocks.Persistence;
using MyWorkplace.Customers.Persistence;

namespace MyWorkplace.Customers.Features;

/// <summary>
/// EN: <c>GET /customers</c> — one page of the caller's customers, optionally searched (ADR-016).
/// TR: <c>GET /customers</c> — çağıranın müşterilerinden bir sayfa, isteğe bağlı aramayla (ADR-016).
/// </summary>
public static class ListCustomers
{
    /// <summary>
    /// EN: Maps the endpoint on the /customers group.
    /// TR: Uç noktayı /customers grubunda tanımlar.
    /// </summary>
    /// <param name="group">EN: The /customers group. TR: /customers grubu.</param>
    /// <returns>EN: The endpoint builder. TR: Uç nokta builder'ı.</returns>
    public static RouteHandlerBuilder MapListCustomers(this IEndpointRouteBuilder group) =>
        group.MapGet("", HandleAsync)
            .WithName("ListCustomers")
            .WithSummary("EN: List customers | TR: Müşterileri listele")
            .WithDescription(
                "EN: Returns one page of your customers, sorted by name. page ≥ 1 (default 1), pageSize 1–100 " +
                "(default 20). search matches name, email or phone, case-insensitive; % and _ are matched literally. " +
                "TR: Müşterilerinizden ada göre sıralı bir sayfa döner. page ≥ 1 (varsayılan 1), pageSize 1–100 " +
                "(varsayılan 20). search; ad, e-posta veya telefonda büyük/küçük harf duyarsız eşleşir; % ve _ harfiyen aranır.")
            .ProducesValidationProblem();

    /// <summary>
    /// EN: Handles the request; the tenant and soft-delete filters apply to the list and to the count.
    /// TR: İsteği işler; firma ve soft-delete filtreleri hem listeye hem sayıma uygulanır.
    /// </summary>
    /// <param name="page">EN: Page and search parameters. TR: Sayfa ve arama parametreleri.</param>
    /// <param name="db">EN: Customers database. TR: Customers veritabanı.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: The page. TR: Sayfa.</returns>
    public static async Task<Ok<PagedResult<CustomerResponse>>> HandleAsync(
        [AsParameters] PageQuery page,
        CustomersDbContext db,
        CancellationToken cancellationToken)
    {
        var customers = db.Customers.AsQueryable();

        if (page.SearchText is { } text)
        {
            var pattern = SearchPattern.Contains(text);
            customers = customers.Where(c =>
                EF.Functions.ILike(c.Name, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(c.Email!, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(c.Phone!, pattern, SearchPattern.EscapeCharacter));
        }

        var result = await customers
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToPagedResultAsync(CustomerResponse.Projection, page, cancellationToken);

        return TypedResults.Ok(result);
    }
}
