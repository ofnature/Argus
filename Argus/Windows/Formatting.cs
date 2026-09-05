using System;

namespace Argus.Windows;

internal static class Formatting
{
    /// <summary>"1d 3h", "3h 12m", "12m", "45s". Negative spans read as "now".</summary>
    public static string Duration(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
            return "now";
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes}m";
        return $"{(int)span.TotalSeconds}s";
    }

    /// <summary>"31h 05m" style for voyage lengths, hours never rolling into days.</summary>
    public static string VoyageLength(TimeSpan span)
        => $"{(int)span.TotalHours}h {span.Minutes:00}m";

    public static string Number(uint n) => n.ToString("N0");
    public static string Number(int n) => n.ToString("N0");
    public static string Number(long n) => n.ToString("N0");

    public static string LocalTime(DateTime utc)
        => utc.ToLocalTime().ToString("ddd HH:mm");
}
