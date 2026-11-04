namespace SelfKept.Infrastructure.Services.Collect;

/// <summary>
/// Calendar days in a person's time zone. All instants are UTC; a day is the person's local date.
/// An unknown zone id falls back to UTC rather than failing a batch.
/// </summary>
public static class DayBoundaries
{
  public static TimeZoneInfo Resolve(string? timeZoneId)
  {
    if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
    return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out var zone) ? zone : TimeZoneInfo.Utc;
  }

  public static DateOnly LocalDay(DateTime utc, TimeZoneInfo zone) =>
    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone));

  /// <summary>The UTC instants [start, end) that make up the local day.</summary>
  public static (DateTime StartUtc, DateTime EndUtc) UtcWindow(DateOnly day, TimeZoneInfo zone) =>
    (LocalMidnightUtc(day, zone), LocalMidnightUtc(day.AddDays(1), zone));

  public static DateOnly MonthStart(DateTime utc) => new(utc.Year, utc.Month, 1);

  /// <summary>00:00 UTC on the first of the month, the lower bound of its sample partition.</summary>
  public static DateTime MonthStartUtc(DateOnly month) => new(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc);

  public static DateTime AsUtc(DateTime value) => value.Kind switch
  {
    DateTimeKind.Utc => value,
    DateTimeKind.Local => value.ToUniversalTime(),
    _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
  };

  private static DateTime LocalMidnightUtc(DateOnly day, TimeZoneInfo zone)
  {
    var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
    // A zone that switches at midnight has no 00:00 that day; the day then starts at 01:00.
    if (zone.IsInvalidTime(local)) local = local.AddHours(1);
    return TimeZoneInfo.ConvertTimeToUtc(local, zone);
  }
}
