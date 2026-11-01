using System.Globalization;

namespace RaceDay.Web.Models.Events;

public static class EntryFeeFormatter
{
    private static readonly CultureInfo SouthAfrica = CultureInfo.GetCultureInfo("en-ZA");

    public static string Format(decimal fee) =>
        fee == 0
            ? "R0.00 — Free"
            : $"R{fee.ToString("0.00", SouthAfrica)}";

    public static string FormatRange(decimal? minimumEntryFee, decimal? maximumEntryFee, decimal? entryFee)
    {
        if (minimumEntryFee is decimal minimum)
        {
            if (maximumEntryFee is decimal maximum && maximum != minimum)
            {
                return $"{Format(minimum)}–{Format(maximum)}";
            }

            return Format(minimum);
        }

        return entryFee is decimal fee ? Format(fee) : "Fee unavailable";
    }
}
