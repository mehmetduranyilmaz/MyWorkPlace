namespace MyWorkplace.BuildingBlocks.Identity;

/// <summary>
/// EN: The user making the current request. Filled from the validated JWT in T-008; empty for anonymous requests.
/// TR: Mevcut isteği yapan kullanıcı. T-008'de doğrulanmış JWT'den doldurulur; anonim isteklerde boştur.
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
}
