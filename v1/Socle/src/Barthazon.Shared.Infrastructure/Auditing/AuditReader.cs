using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Shared.Infrastructure.Auditing;

/// <summary>Lecture du journal des modifications.</summary>
public sealed class AuditReader(IDbContextFactory<SocleDbContext> contextFactory) : IAuditReader
{
    public async Task<PagedResult<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entries = context.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(e => e.EntityType == query.EntityType);
        }

        if (query.EntityOrParentId is { } id)
        {
            entries = entries.Where(e => e.EntityId == id || e.ParentId == id);
        }

        if (!string.IsNullOrWhiteSpace(query.UserName))
        {
            entries = entries.Where(e => e.UserName == query.UserName);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(e => e.At >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(e => e.At <= to);
        }

        var total = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(e => e.At)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditEntry>(items, total);
    }
}
