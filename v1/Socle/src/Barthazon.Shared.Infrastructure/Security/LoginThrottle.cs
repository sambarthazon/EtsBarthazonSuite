using System.Collections.Concurrent;

namespace Barthazon.Shared.Infrastructure.Security;

/// <summary>
/// Freine les essais de mots de passe : après <see cref="MaxFailures"/> échecs consécutifs pour un même
/// couple (adresse, identifiant), les tentatives sont refusées pendant <see cref="LockoutDuration"/>.
/// Mémoire seule : un redémarrage remet les compteurs à zéro (suffisant sur un réseau interne).
/// </summary>
public sealed class LoginThrottle(TimeProvider timeProvider)
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private const int MaxEntries = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Temps d'attente restant avant une nouvelle tentative, ou null si la tentative est permise.</summary>
    public TimeSpan? RetryAfter(string? remoteAddress, string? userName)
    {
        if (!_entries.TryGetValue(Key(remoteAddress, userName), out var entry) || entry.LockedUntil is not { } until)
        {
            return null;
        }

        var remaining = until - timeProvider.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    public void RecordFailure(string? remoteAddress, string? userName)
    {
        if (_entries.Count > MaxEntries)
        {
            _entries.Clear();
        }

        var now = timeProvider.GetUtcNow();
        _entries.AddOrUpdate(
            Key(remoteAddress, userName),
            _ => new Entry(1, null),
            (_, previous) =>
            {
                // Après la fin d'un blocage, le compteur repart de zéro.
                var failures = previous.LockedUntil is { } until && until <= now ? 1 : previous.Failures + 1;
                return new Entry(failures, failures >= MaxFailures ? now + LockoutDuration : null);
            });
    }

    public void RecordSuccess(string? remoteAddress, string? userName) => _entries.TryRemove(Key(remoteAddress, userName), out _);

    private static string Key(string? remoteAddress, string? userName) =>
        $"{remoteAddress}|{(userName ?? string.Empty).Trim().ToUpperInvariant()}";

    private sealed record Entry(int Failures, DateTimeOffset? LockedUntil);
}
