using System.Globalization;

namespace RunReport;

internal static class InvariantText
{
    /// <summary>Formats an interpolated string with the invariant culture, so reports read the same on every machine.</summary>
    public static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
