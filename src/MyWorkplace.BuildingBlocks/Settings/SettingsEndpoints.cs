using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyWorkplace.BuildingBlocks.Http;
using MyWorkplace.Abstractions.Identity;

namespace MyWorkplace.BuildingBlocks.Settings;

/// <summary>
/// EN: <c>GET</c> / <c>PUT {module}/settings</c> for any settings class, in one line (ADR-018).
/// TR: Herhangi bir ayar sınıfı için tek satırda <c>GET</c> / <c>PUT {modül}/settings</c> (ADR-018).
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>
    /// EN: Maps the settings endpoints under <paramref name="group"/>: reading needs <paramref name="readPermission"/>
    ///     (the UI must know how the module behaves), changing needs <c>settings.manage</c>.
    /// TR: Ayar uç noktalarını <paramref name="group"/> altında tanımlar: okumak <paramref name="readPermission"/> ister
    ///     (arayüz modülün nasıl davrandığını bilmelidir), değiştirmek <c>settings.manage</c> ister.
    /// </summary>
    /// <typeparam name="TSettings">EN: The module's settings class. TR: Modülün ayar sınıfı.</typeparam>
    /// <param name="group">EN: The module's route group, e.g. /inventory. TR: Modülün rota grubu, ör. /inventory.</param>
    /// <param name="readPermission">EN: Permission for reading. TR: Okuma izni.</param>
    /// <returns>EN: The /settings group. TR: /settings grubu.</returns>
    public static RouteGroupBuilder MapModuleSettings<TSettings>(this IEndpointRouteBuilder group, string readPermission)
        where TSettings : class, IModuleSettings, new()
    {
        var settings = group.MapGroup("/settings").WithTags("Settings");

        settings.MapGet(
                "",
                (ITenantSettings<TSettings> store, HttpContext http, CancellationToken ct) => GetAsync(store, http, ct))
            .WithName($"Get{TSettings.Module}Settings")
            .RequireAuthorization(readPermission)
            .WithSummary("EN: Get the settings | TR: Ayarları getir")
            .WithDescription(
                "EN: Returns your company's settings of this module and their version in ETag. Values you never " +
                "changed are the defaults. " +
                "TR: Firmanızın bu modüldeki ayarlarını ve sürümünü ETag'de döner. Hiç değiştirmediğiniz değerler " +
                "varsayılanlardır.");

        settings.MapPut(
                "",
                (ITenantSettings<TSettings> store, HttpContext http, CancellationToken ct) => SaveAsync(store, http, ct))
            // EN: The body is read by the handler (see SaveAsync), so it is declared for the API docs here.
            // TR: Gövde handler tarafından okunur (bkz. SaveAsync); bu yüzden API dokümanı için burada bildirilir.
            .Accepts<TSettings>("application/json")
            .WithName($"Update{TSettings.Module}Settings")
            .RequireAuthorization(CorePermissions.SettingsManage)
            .WithSummary("EN: Change the settings | TR: Ayarları değiştir")
            .WithDescription(
                "EN: Replaces all settings of this module; omitted values take their defaults. Requires If-Match with " +
                "the ETag you read (428 without it, 412 if someone saved in the meantime). Takes effect immediately. " +
                "TR: Bu modülün tüm ayarlarını değiştirir; gönderilmeyen değerler varsayılanını alır. Okuduğunuz ETag " +
                "ile If-Match gerekir (yoksa 428, arada biri kaydettiyse 412). Hemen geçerli olur.")
            .ProducesValidationProblem();

        return settings;
    }

    /// <summary>
    /// EN: Handles <c>GET</c>.
    /// TR: <c>GET</c> isteğini işler.
    /// </summary>
    /// <typeparam name="TSettings">EN: Settings class. TR: Ayar sınıfı.</typeparam>
    /// <param name="settings">EN: Settings store. TR: Ayar deposu.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200 with the settings. TR: Ayarlarla 200.</returns>
    private static async Task<Ok<TSettings>> GetAsync<TSettings>(
        ITenantSettings<TSettings> settings,
        HttpContext http,
        CancellationToken cancellationToken)
        where TSettings : class, IModuleSettings, new()
    {
        var current = await settings.GetWithVersionAsync(cancellationToken);
        http.Response.SetETag(current.Version);
        return TypedResults.Ok(current.Value);
    }

    /// <summary>
    /// EN: Handles <c>PUT</c>: precondition, body, validation, save. The body is read here instead of being bound as a
    ///     parameter: a parameter typed with a generic type argument crashes the ASP.NET route analyzer (AD0001), and
    ///     reading it ourselves turns an unknown value (e.g. <c>"Maybe"</c>) into a field-level 400.
    /// TR: <c>PUT</c> isteğini işler: ön koşul, gövde, doğrulama, kaydetme. Gövde parametre olarak bağlanmak yerine burada
    ///     okunur: generic tip argümanıyla tiplenmiş bir parametre ASP.NET rota analizörünü çökertir (AD0001); kendimiz okumak
    ///     ise bilinmeyen bir değeri (ör. <c>"Maybe"</c>) alan bazlı bir 400'e çevirir.
    /// </summary>
    /// <typeparam name="TSettings">EN: Settings class. TR: Ayar sınıfı.</typeparam>
    /// <param name="settings">EN: Settings store. TR: Ayar deposu.</param>
    /// <param name="http">EN: Current request. TR: Mevcut istek.</param>
    /// <param name="cancellationToken">EN: Request cancellation. TR: İstek iptali.</param>
    /// <returns>EN: 200, 400, 412 or 428. TR: 200, 400, 412 veya 428.</returns>
    private static async Task<Results<Ok<TSettings>, ValidationProblem, ProblemHttpResult>> SaveAsync<TSettings>(
        ITenantSettings<TSettings> settings,
        HttpContext http,
        CancellationToken cancellationToken)
        where TSettings : class, IModuleSettings, new()
    {
        if (!http.Request.TryReadIfMatch(out var expectedVersion, out var preconditionProblem))
        {
            return preconditionProblem;
        }

        var json = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        TSettings? values;
        try
        {
            values = http.Request.HasJsonContentType()
                ? await http.Request.ReadFromJsonAsync<TSettings>(json, cancellationToken)
                : null;
        }
        catch (JsonException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.Path is { Length: > 2 } path ? path[2..] : "body"] = ["The value is not valid."],
            });
        }

        if (values is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["A JSON body with the settings is required."],
            });
        }

        // EN: DataAnnotations on the settings class, checked here: the validation source generator can't see a generic
        //     endpoint's type either.
        // TR: Ayar sınıfındaki DataAnnotations burada kontrol edilir: doğrulama kaynak üreteci de generic bir uç noktanın
        //     tipini göremez.
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(values, new ValidationContext(values), errors, validateAllProperties: true))
        {
            return TypedResults.ValidationProblem(errors
                .SelectMany(e => e.MemberNames.DefaultIfEmpty(""), (e, member) => (member, e.ErrorMessage ?? ""))
                .GroupBy(e => e.member, e => e.Item2)
                .ToDictionary(g => g.Key, g => g.ToArray()));
        }

        var version = await settings.SaveAsync(values, expectedVersion, cancellationToken);
        if (version is null)
        {
            return ETags.PreconditionFailed();
        }

        http.Response.SetETag(version.Value);
        return TypedResults.Ok(values);
    }
}
