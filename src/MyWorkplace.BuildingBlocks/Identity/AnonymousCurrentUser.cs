namespace MyWorkplace.BuildingBlocks.Identity;

/// <summary>
/// EN: Default current user until authentication is wired in (T-008): nobody is signed in.
/// TR: Kimlik doğrulama bağlanana kadar (T-008) varsayılan kullanıcı: kimse giriş yapmamış.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId => null;

    /// <inheritdoc />
    public Guid? TenantId => null;
}
