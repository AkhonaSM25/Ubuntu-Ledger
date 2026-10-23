using System.Globalization;

namespace UbuntuLedger.Models;

// Display formatting shared by views and services.
public static class Format
{
    private const char Nbsp = ' ';
    public static readonly string[] Mon = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    // R 12 500.00 with non-breaking spaces; negative amounts use a true minus sign.
    public static string Zar(decimal n)
    {
        var abs = Math.Abs(n).ToString("N2", CultureInfo.InvariantCulture).Replace(',', Nbsp);
        return (n < 0 ? "−" : "") + "R" + Nbsp + abs;
    }

    public static string Date(DateOnly d) => $"{d.Day} {Mon[d.Month - 1]} {d.Year}";

    public static string LongDate(DateOnly d) =>
        d.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);

    public static string MonthYear(DateOnly d) => d.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    // 6000 -> 6k, 1200 -> 1.2k (chart labels).
    public static string Kilo(decimal n)
    {
        if (n <= 0) return "";
        if (n < 1000) return Math.Round(n).ToString(CultureInfo.InvariantCulture);
        var k = n / 1000m;
        return (k % 1 == 0 ? k.ToString("0", CultureInfo.InvariantCulture) : k.ToString("0.0", CultureInfo.InvariantCulture)) + "k";
    }

    // Plain number for form inputs, e.g. 1100 or 366.67.
    public static string Input(decimal n) => n.ToString("0.##", CultureInfo.InvariantCulture);
}
