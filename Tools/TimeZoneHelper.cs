using System;

namespace NoviSad.SokoBot.Tools;

public static class TimeZoneHelper {
    private static readonly TimeZoneInfo CentralEuropeanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

    public static DateTimeOffset ToCentralEuropeanTime(DateTimeOffset offset) {
        return TimeZoneInfo.ConvertTime(offset, CentralEuropeanTimeZone);
    }

    public static DateTimeOffset ToCentralEuropeanTime(DateTime dateTime) {
        return new DateTimeOffset(dateTime, CentralEuropeanTimeZone.GetUtcOffset(dateTime));
    }

    public static DateTimeOffset ToCentralEuropeanTime(DateOnly date) {
        var dateTime = date.ToDateTime(default); // DateTimeKind.Unspecified
        return ToCentralEuropeanTime(dateTime);
    }
}
