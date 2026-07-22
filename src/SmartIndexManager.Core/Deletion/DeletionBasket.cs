using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.Core.Deletion;

public sealed class DeletionBasket
{
    private readonly List<DeletionBasketEntry> _entries = [];

    public IReadOnlyList<DeletionBasketEntry> Entries => _entries.AsReadOnly();

    public BasketResult Add(IndexModel index, SafetyAssessment safety, ConfidenceScore? score = null)
    {
        if (safety.Eligibility != DeletionEligibility.Deletable)
            return new BasketResult(false, "Index is not deletable.");
        if (_entries.Any(e => Matches(e.Index, index)))
            return new BasketResult(false, "Index is already in the basket.");
        _entries.Add(new DeletionBasketEntry(index, safety, score));
        return new BasketResult(true, null);
    }

    public void Remove(IndexModel index)
        => _entries.RemoveAll(e => Matches(e.Index, index));

    public void Clear() => _entries.Clear();

    private static bool Matches(IndexModel a, IndexModel b)
        => string.Equals(a.Database, b.Database, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Table, b.Table, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
