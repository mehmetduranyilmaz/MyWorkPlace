using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Identity;
using MyWorkplace.Abstractions.Identity;
using MyWorkplace.Identity.Domain;
using MyWorkplace.Identity.Persistence;

namespace MyWorkplace.Identity.Features.Users;

/// <summary>
/// EN: Steps shared by the endpoints that change or remove users: reading the caller fresh from the database and
///     serializing changes per company so the last-Owner rule can't be raced (ADR-022).
/// TR: Kullanıcı değiştiren veya kaldıran uç noktaların ortak adımları: çağıranı veritabanından taze okumak ve
///     son Sahip kuralı yarışla aşılamasın diye değişiklikleri firma bazında sıraya sokmak (ADR-022).
/// </summary>
internal static class UserAdministration
{
    /// <summary>
    /// EN: The caller as stored now. The token's permissions may be up to 15 minutes old; the anti-escalation rule
    ///     uses the current roles instead, so a just-demoted Admin can't grant what they no longer have.
    /// TR: Çağıranın şu anki kaydı. Token'daki izinler 15 dakikaya kadar eski olabilir; yetki yükseltme kuralı bunun yerine
    ///     güncel rolleri kullanır, böylece rolü az önce düşürülen bir Admin artık sahip olmadığı izni veremez.
    /// </summary>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="currentUser">EN: The signed-in user. TR: Giriş yapmış kullanıcı.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The caller, or null if they were removed meanwhile. TR: Çağıran; bu arada kaldırıldıysa null.</returns>
    public static Task<User?> FindCallerAsync(
        IdentityDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);

    /// <summary>
    /// EN: Runs <paramref name="operation"/> in a transaction that first locks the caller's company row. Two Owners
    ///     demoting each other at the same moment would otherwise both see "another Owner exists" and leave the
    ///     company with none. The lock lives only for this short transaction — unlike the edit-time lock ADR-017 rejects.
    ///     Wrapped in the execution strategy, so a transient failure re-runs the whole unit.
    /// TR: <paramref name="operation"/>'ı, önce çağıranın firma satırını kilitleyen bir transaction içinde çalıştırır. Aksi halde
    ///     aynı anda birbirinin rolünü düşüren iki Sahip de "başka Sahip var" görür ve firma Sahip'siz kalırdı. Kilit sadece bu
    ///     kısa transaction boyunca yaşar — ADR-017'nin reddettiği düzenleme süresince tutulan kilitten farklıdır.
    ///     Execution strategy ile sarılıdır; geçici bir hatada bütün birim yeniden çalışır.
    /// </summary>
    /// <typeparam name="TResult">EN: The endpoint result. TR: Uç nokta sonucu.</typeparam>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="currentUser">EN: The signed-in user. TR: Giriş yapmış kullanıcı.</param>
    /// <param name="operation">EN: The change; commits when it returns. TR: Değişiklik; döndüğünde commit edilir.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: The operation's result. TR: İşlemin sonucu.</returns>
    public static Task<TResult> InCompanyLockAsync<TResult>(
        IdentityDbContext db,
        ICurrentUser currentUser,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlAsync(
                    $"SELECT id FROM tenants WHERE id = {currentUser.TenantId} FOR UPDATE", ct);

                var result = await operation(ct);
                await transaction.CommitAsync(ct);
                return result;
            },
            cancellationToken);

    /// <summary>
    /// EN: Whether a live Owner other than <paramref name="userId"/> exists in the caller's company.
    ///     Call it inside <see cref="InCompanyLockAsync"/> only.
    /// TR: Çağıranın firmasında <paramref name="userId"/> dışında canlı bir Sahip olup olmadığı.
    ///     Sadece <see cref="InCompanyLockAsync"/> içinde çağırın.
    /// </summary>
    /// <param name="db">EN: Identity database. TR: Identity veritabanı.</param>
    /// <param name="userId">EN: The user being demoted or removed. TR: Rolü düşürülen veya kaldırılan kullanıcı.</param>
    /// <param name="cancellationToken">EN: Cancellation token. TR: İptal belirteci.</param>
    /// <returns>EN: True if another Owner remains. TR: Başka bir Sahip kalıyorsa true.</returns>
    public static Task<bool> HasAnotherOwnerAsync(IdentityDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id != userId && u.Roles.Contains(DefaultRoles.Owner), cancellationToken);
}
