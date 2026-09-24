namespace MyWorkplace.BuildingBlocks.Identity;

/// <summary>
/// EN: Nobody is signed in. Used where there is no request at all, e.g. <c>dotnet ef</c> at design time.
/// TR: Kimse giriş yapmamış. Hiç istek olmayan yerlerde kullanılır, ör. tasarım zamanında <c>dotnet ef</c>.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId => null;

    /// <inheritdoc />
    public Guid? TenantId => null;

    /// <inheritdoc />
    public string? Plan => null;
}
