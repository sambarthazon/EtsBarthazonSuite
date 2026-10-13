using Barthazon.Referential.Contracts;

namespace Barthazon.Referential.Core.Services;

/// <summary>Additionne les utilisations déclarées par tous les modules.</summary>
internal sealed class UsageCounter(IEnumerable<IReferentialUsageProvider> providers)
{
    public async Task<IReadOnlyDictionary<Guid, int>> CountAsync(ReferentialItemKind kind, CancellationToken cancellationToken)
    {
        var total = new Dictionary<Guid, int>();
        foreach (var provider in providers)
        {
            foreach (var (id, count) in await provider.CountUsagesAsync(kind, cancellationToken))
            {
                total[id] = total.GetValueOrDefault(id) + count;
            }
        }

        return total;
    }
}
