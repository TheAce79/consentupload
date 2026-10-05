using System.Globalization;

namespace ConsentSyncCore.Services;

/// <summary>Parses the appointment-time formats produced by PDF rosters and AbleAssess exports.</summary>
public static class AppointmentTime
{
    private static readonly string[] Formats = ["h:mm tt", "hh:mm tt", "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "H'h'mm", "HH'h'mm"];

    public static bool TryParse(string? value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Trim().Replace('.', ':').ToUpperInvariant();
        return TimeOnly.TryParseExact(normalized, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time);
    }

    public static int SortKey(string? value) => TryParse(value, out TimeOnly time) ? time.Hour * 60 + time.Minute : int.MaxValue;

    public static string Display(string? value) => TryParse(value, out TimeOnly time)
        ? time.ToString("hh:mm tt", CultureInfo.InvariantCulture)
        : "Unscheduled";
}
