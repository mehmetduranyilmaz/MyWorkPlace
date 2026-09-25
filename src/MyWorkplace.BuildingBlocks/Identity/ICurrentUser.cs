namespace MyWorkplace.BuildingBlocks.Identity;

/// <summary>
/// EN: The user making the current request, read from the validated JWT (<see cref="HttpCurrentUser"/>);
///     empty for anonymous requests and outside of requests (startup, background work).
/// TR: Mevcut isteği yapan kullanıcı, doğrulanmış JWT'den okunur (<see cref="HttpCurrentUser"/>);
///     anonim isteklerde ve istek dışında (açılış, arka plan işleri) boştur.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// EN: User id, or null when nobody is signed in.
    /// TR: Kullanıcı kimliği; giriş yapan yoksa null.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// EN: Company of the user, or null. With null, tenant-filtered queries return nothing (safe default).
    /// TR: Kullanıcının firması veya null. Null iken firma filtreli sorgular hiçbir şey döndürmez (güvenli varsayılan).
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// EN: Plan of the company as carried by the token (a plan name defined by the product), or null.
    /// TR: Token'da taşındığı haliyle firmanın planı (ürünün tanımladığı bir plan adı) ya da null.
    /// </summary>
    string? Plan { get; }
}
