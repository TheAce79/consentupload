using System.Text.RegularExpressions;

namespace ConsentSyncCore.Services;

/// <summary>Converts source-specific vaccine and appointment labels into eligibility categories.</summary>
public static class VaccineTypeNormalizer
{
    public const string Influenza = "Influenza";
    public const string Covid19 = "COVID-19";
    public const string CovidAndInfluenza = "COVID-19 and Influenza";
    private const string Unknown = "Unknown";
    private const string Other = "Other / Autre";

    private static readonly Regex PreschoolRegex = new(@"\b(?:ps|pr[eé]scolaire|preschool)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OtherRegex = new(@"\b(?:autres?|other|divers|rattrapage)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Normalize(string? rawVaccineType)
    {
        if (string.IsNullOrWhiteSpace(rawVaccineType)) return Unknown;

        string value = rawVaccineType.Trim();
        string? respiratory = NormalizeRespiratory(value);
        if (respiratory is not null) return respiratory;
        if (string.Equals(value, Unknown, StringComparison.OrdinalIgnoreCase)) return Unknown;
        if (string.Equals(value, "ETS Unknown", StringComparison.OrdinalIgnoreCase)) return "ETS Unknown";
        if (string.Equals(value, "Preschool Appointment", StringComparison.OrdinalIgnoreCase)) return "Preschool Appointment";
        if (string.Equals(value, "2 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "2 Month Appointment";
        if (string.Equals(value, "4 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "4 Month Appointment";
        if (string.Equals(value, "6 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "6 Month Appointment";
        if (string.Equals(value, "12 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "12 Month Appointment";
        if (string.Equals(value, "18 Month Appointment", StringComparison.OrdinalIgnoreCase)) return "18 Month Appointment";
        if (IsEtsPlus(value)) return "ETS+";
        if (IsEts(value)) return "ETS";
        if (string.Equals(value, "Catchup Appointment", StringComparison.OrdinalIgnoreCase)) return "Catchup Appointment";
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

    public static string? NormalizeRespiratory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        bool flu = (value.Contains("influenza", StringComparison.OrdinalIgnoreCase) &&
                    !value.Contains("haemophilus", StringComparison.OrdinalIgnoreCase)) ||
                   value.Contains("grippe", StringComparison.OrdinalIgnoreCase);
        bool covid = value.Contains("covid", StringComparison.OrdinalIgnoreCase);
        if (flu && covid) return CovidAndInfluenza;
        if (flu) return Influenza;
        return covid ? Covid19 : null;
    }

    private static bool MatchesMilestone(string value, int months) => Regex.IsMatch(
        value,
        $@"\b{months}\s*(?:mois|months?|m)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsEtsPlus(string value) =>
        value.Contains("ETS+", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ETS Plus", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ETS +", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ETS/", StringComparison.OrdinalIgnoreCase);

    private static bool IsEts(string value) =>
        (string.Equals(value, "ETS", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("ETS ", StringComparison.OrdinalIgnoreCase)) &&
        !IsEtsPlus(value) &&
        !value.Contains("Plus", StringComparison.OrdinalIgnoreCase);
}
