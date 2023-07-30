using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NoviSad.SokoBot.Tools;

namespace NoviSad.SokoBot.Data.Converters;

public class DateTimeOffsetToUtcMinutesConverter : ValueConverter<DateTimeOffset, long> {
    public DateTimeOffsetToUtcMinutesConverter()
        : base(
            x => x.UtcDateTime.Ticks / TimeZoneHelper.TicksPerMinute,
            x => new DateTimeOffset(x * TimeZoneHelper.TicksPerMinute, TimeSpan.Zero)
        ) { }
}
