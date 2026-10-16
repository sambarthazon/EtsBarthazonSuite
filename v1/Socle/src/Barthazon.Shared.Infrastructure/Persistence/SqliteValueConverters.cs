using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>
/// SQLite ne sait ni trier ni comparer des <see cref="DateTimeOffset"/> : on les stocke en ticks UTC (entier),
/// ce qui conserve l'ordre chronologique.
/// </summary>
internal sealed class DateTimeOffsetToUtcTicksConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
