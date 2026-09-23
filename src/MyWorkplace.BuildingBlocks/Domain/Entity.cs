namespace MyWorkplace.BuildingBlocks.Domain;

/// <summary>
/// EN: Base class of every persisted entity. Technical identity only — no business behavior belongs here (ADR-011).
/// TR: Kalıcı her entity'nin temel sınıfı. Sadece teknik kimlik — buraya iş davranışı konmaz (ADR-011).
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// EN: Unique id, generated as UUID v7: unguessable, yet time-ordered so database indexes stay compact.
    /// TR: UUID v7 olarak üretilen benzersiz kimlik: tahmin edilemez ama zamana göre sıralı, bu yüzden indeksler derli toplu kalır.
    /// </summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();
}
