namespace MyWorkplace.BuildingBlocks.Domain;

/// <summary>
/// EN: Marks data that belongs to one company. Queries are filtered to the current tenant automatically (ADR-004).
/// TR: Tek bir firmaya ait veriyi işaretler. Sorgular otomatik olarak aktif firmaya daraltılır (ADR-004).
/// </summary>
public interface ITenantOwned
{
    /// <summary>
    /// EN: Owning company. Set on insert (from the current user unless given explicitly); can never change afterwards.
    /// TR: Sahip firma. Eklemede atanır (açıkça verilmediyse giriş yapan kullanıcıdan); sonradan asla değişemez.
    /// </summary>
    Guid TenantId { get; set; }
}
