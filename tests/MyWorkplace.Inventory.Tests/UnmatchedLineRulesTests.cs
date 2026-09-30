using MyWorkplace.Inventory.Domain;

namespace MyWorkplace.Inventory.Tests;

/// <summary>
/// EN: Review rules of an unmatched order line as plain domain logic (T-042): only an open entry can be settled.
/// TR: Saf domain mantığı olarak eşleşmeyen bir sipariş satırının inceleme kuralları (T-042): sadece açık bir kayıt çözülebilir.
/// </summary>
public sealed class UnmatchedLineRulesTests
{
    [Fact]
    public void Resolve_OpenEntry_RemembersTheItem()
    {
        var line = new UnmatchedOrderLine();
        var itemId = Guid.CreateVersion7();

        line.Resolve(itemId);

        Assert.Equal((UnmatchedLineStatus.Resolved, itemId), (line.Status, line.ResolvedStockItemId));
    }

    [Fact]
    public void Dismiss_OpenEntry_KeepsATrimmedNote()
    {
        var line = new UnmatchedOrderLine();

        line.Dismiss("  a service  ");

        Assert.Equal((UnmatchedLineStatus.Dismissed, "a service"), (line.Status, line.Note));
    }

    [Fact]
    public void ASettledEntry_CantBeSettledAgain()
    {
        var resolved = new UnmatchedOrderLine();
        resolved.Resolve(Guid.CreateVersion7());
        var dismissed = new UnmatchedOrderLine();
        dismissed.Dismiss(null);

        Assert.Throws<InvalidOperationException>(() => resolved.Resolve(Guid.CreateVersion7()));
        Assert.Throws<InvalidOperationException>(() => resolved.Dismiss(null));
        Assert.Throws<InvalidOperationException>(() => dismissed.Resolve(Guid.CreateVersion7()));
    }
}
