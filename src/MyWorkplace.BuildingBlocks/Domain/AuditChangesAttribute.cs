namespace MyWorkplace.BuildingBlocks.Domain;

/// <summary>
/// EN: Every change of the marked property is written to the audit log with its old and new value.
///     Never use it on sensitive data such as password hashes or keys.
/// TR: İşaretli alanın her değişikliği eski ve yeni değeriyle denetim günlüğüne yazılır.
///     Parola hash'i veya anahtar gibi hassas verilerde asla kullanılmaz.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditChangesAttribute : Attribute;
