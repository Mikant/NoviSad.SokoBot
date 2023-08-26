using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace NoviSad.SokoBot.Data.Converters;

public class DateTimeOffsetToUtcMinutesConverter : ValueConverter<DateTimeOffset, long> {
    private const long TicksPerMillisecond = 10000;
    private const long TicksPerSecond = TicksPerMillisecond * 1000;
    private const long TicksPerMinute = TicksPerSecond * 60;

    public DateTimeOffsetToUtcMinutesConverter()
        : base(
            x => x.UtcDateTime.Ticks / TicksPerMinute,
            x => new DateTimeOffset(x * TicksPerMinute, TimeSpan.Zero)
        ) {
    }
}
