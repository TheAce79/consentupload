using System.Text.RegularExpressions;

namespace ConsentSyncCore.Services;

/// <summary>Converts source-specific vaccine and appointment labels into eligibility categories.</summary>
public static class VaccineTypeNormalizer
{
    private const string Unknown = "Unknown";
    private const string Other = "Other / Autre";

    private static readonly Regex PreschoolRegex = new(@"\b(?:ps|pr[eé]scolaire|preschool)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OtherRegex = new(@"\b(?:autres?|other|divers|rattrapage)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Normalize(string? rawVaccineType)
    {
        if (string.IsNullOrWhiteSpace(rawVaccineType)) return Unknown;

        string value = rawVaccineType.Trim();
        if (string.Equals(value, Unknown, StringComparison.OrdinalIgnoreCase)) return Unknown;
        if (string.Equals(value, "Preschool Appointment", StringComparison.OrdinalIgnoreCase)) return "Preschool Appointment";
        if (string.Equals(value, "2 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "2 Month Appointment";
        if (string.Equals(value, "4 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "4 Month Appointment";
        if (string.Equals(value, "6 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "6 Month Appointment";
        if (string.Equals(value, "12 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "12 Month Appointment";
        if (string.Equals(value, "18 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "18 Month Appointment";
        if (string.Equals(value, Other, StringComparison.OrdinalIgnoreCase)) return Other;

        if (PreschoolRegex.IsMatch(value)) return "Preschool Appointment";
        if (MatchesMilestone(value, 18)) return "18 Month Appointment";
        if (MatchesMilestone(value, 12)) return "12 Month Appointment";
        if (MatchesMilestone(value, 6)) return "6 Month Appointment";
        if (MatchesMilestone(value, 4)) return "4 Month Appointment";
        if (MatchesMilestone(value, 2)) return "2 Month Appointment";
        if (OtherRegex.IsMatch(value)) return Other;

        return Other;
    }

    private static bool MatchesMilestone(string value, int months) => Regex.IsMatch(
        value,
        $@"\b{months}\s*(?:mois|months?|m)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
