using System.Globalization;

namespace Fannyfique.Core.Formatting;

/// <summary>Formatage des nombres, tailles et dates pour l'interface (français).</summary>
public static class Format
{
    public static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly string[] Units = ["o", "Ko", "Mo", "Go", "To"];

    /// <summary>« 512 o », « 25,4 Mo », « 1,2 To ».</summary>
    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (Math.Abs(value) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.#";
        return $"{value.ToString(format, French)} {Units[unit]}";
    }

    /// <summary>« 12 345 ».</summary>
    public static string Count(int count) => count.ToString("N0", French);

    public static string Plural(int count, string singular, string? plural = null) =>
        $"{Count(count)} {(count > 1 ? plural ?? singular + "s" : singular)}";

    public static string Percent(double ratio) => (ratio * 100).ToString(ratio is > 0.99 and < 1 ? "0.#" : "0", French) + " %";

    /// <summary>« 12 mai 2024 », « 12 – 14 mai 2024 », « 28 avr. – 2 mai 2024 », « 30 déc. 2023 – 2 janv. 2024 ».</summary>
    public static string DateRange(DateTime? first, DateTime? last)
    {
        if (first is not { } a || last is not { } b)
        {
            return first?.ToString("d MMMM yyyy", French) ?? last?.ToString("d MMMM yyyy", French) ?? "Date inconnue";
        }

        if (a.Date == b.Date)
        {
            return a.ToString("d MMMM yyyy", French);
        }

        if (a.Year == b.Year && a.Month == b.Month)
        {
            return $"{a.Day} – {b.ToString("d MMMM yyyy", French)}";
        }

        if (a.Year == b.Year)
        {
            return $"{a.ToString("d MMM", French)} – {b.ToString("d MMM yyyy", French)}";
        }

        return $"{a.ToString("d MMM yyyy", French)} – {b.ToString("d MMM yyyy", French)}";
    }

    public static string Date(DateTime? date) => date?.ToString("dd/MM/yyyy", French) ?? "jamais";

    public static string DateTime(DateTime? date) => date?.ToString("dd/MM/yyyy HH:mm:ss", French) ?? "—";

    /// <summary>« 3 min 05 s », « 42 s ».</summary>
    public static string Duration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours} h {duration.Minutes:00} min"
        : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s" : $"{Math.Max(0, duration.Seconds)} s";
}
