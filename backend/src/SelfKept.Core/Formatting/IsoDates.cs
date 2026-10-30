using System.Globalization;

namespace SelfKept.Core.Formatting;

/// <summary>
/// The one way a calendar day is written for machines: evidence, audit details, exports and
/// URLs. Always the invariant culture, so a host set to another calendar never writes a
/// different year.
/// </summary>
public static class IsoDates
{
  public const string Format = "yyyy-MM-dd";

  public static string ToIso(this DateOnly day) => day.ToString(Format, CultureInfo.InvariantCulture);
}
