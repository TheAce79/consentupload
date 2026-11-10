using System.Globalization;
using System.Text.RegularExpressions;
using ConsentSyncCore.Models;
using ConsentSyncCore.Services.Csv;

namespace ConsentSyncCore.Services.Eligibility;

/// <summary>Applies the configured administrative NB appointment checks to parsed PHIS history.</summary>
public sealed class EligibilityEvaluationService
{
    public EligibilityEvaluationResult EvaluateRecord(
        CohortReviewRow record,
        ClientImmunizationHistory? history,
        DateTime cohortDate,
        string jurisdiction = "NB")
    {
        ArgumentNullException.ThrowIfNull(record);

        DateTime? dateOfBirth = ParseDate(record.DateOfBirth);
        bool hasClinicDate = !string.IsNullOrWhiteSpace(record.Source.ClinicDate);
        DateTime? clinicDate = hasClinicDate
            ? ParseDate(record.Source.ClinicDate)
            : (cohortDate == default ? null : cohortDate.Date);
        int doseCount = history?.Records.Count ?? 0;
        DateTime? latestDoseDate = history?.Records.Count > 0
            ? history.Records.Max(item => item.AdministeredDate).Date
            : null;
        var result = new EligibilityEvaluationResult
        {
            ClientId = record.ClientId.Trim(),
            FullName = record.FullName,
            DateOfBirth = dateOfBirth,
            AgeMonths = dateOfBirth.HasValue && clinicDate.HasValue ? CalculateAgeMonths(dateOfBirth.Value, clinicDate.Value) : null,
            VaccineType = record.Source.VaccineType,
            ClinicDate = clinicDate,
            DoseCount = doseCount,
            LatestDoseDate = latestDoseDate,
            HistoryStatus = history is { Records.Count: > 0 } ? "History Found" : "No History Found"
        };

        if (!string.Equals(jurisdiction.Trim(), "NB", StringComparison.OrdinalIgnoreCase))
            return ManualReview(result, $"No administrative eligibility rules are configured for jurisdiction '{jurisdiction}'.");
        if (!dateOfBirth.HasValue)
            return ManualReview(result, "Invalid or missing date of birth. Manual review required.");
        if (!clinicDate.HasValue)
            return ManualReview(result, "Invalid or missing clinic date. Manual review required.");
        string vaccineType = record.Source.VaccineType?.Trim() ?? string.Empty;
        if (vaccineType.Length == 0 || string.Equals(vaccineType, "Unknown", StringComparison.OrdinalIgnoreCase))
            return ManualReview(result, "Unknown vaccine type. Manual review required.");

        DateTime dob = dateOfBirth.Value.Date;
        DateTime clinic = clinicDate.Value.Date;
        if (string.Equals(vaccineType, VaccineTypeNormalizer.Influenza, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(vaccineType, VaccineTypeNormalizer.Covid19, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(vaccineType, VaccineTypeNormalizer.CovidAndInfluenza, StringComparison.OrdinalIgnoreCase))
            return EvaluateRespiratory(result, history, dob, clinic, vaccineType,
                string.IsNullOrWhiteSpace(record.Source.CatalogItem) ? record.Source.AppointmentType : record.Source.CatalogItem);
        if (IsEtsUnknown(vaccineType))
            return ManualReview(result, "Unrecognized or ambiguous ETS appointment type. Manual review required.");
        if (IsEtsPlus(vaccineType) || IsHtaEtsPlus(vaccineType))
            return EvaluateEtsPlus(result, history, dob, clinic, record.Source.CatalogItem);
        if (IsEts(vaccineType) || IsHtaEts(vaccineType))
            return EvaluateEts(result, dob, clinic);
        if (IsAmbiguousEts(vaccineType))
            return ManualReview(result, "Unrecognized or ambiguous ETS appointment type. Manual review required.");
        if (vaccineType.Contains("18 Month", StringComparison.OrdinalIgnoreCase))
            return Evaluate18Month(result, history, dob, clinic);
        if (history is null)
            return ManualReview(result, "No History Found: client is absent from the GNB2009 Excel history. Manual review required.");
        if (vaccineType.Contains("12 Month", StringComparison.OrdinalIgnoreCase))
            return AgeRule(result, clinic >= dob.AddYears(1), "the first birthday", dob, clinic);
        if (vaccineType.Contains("6 Month", StringComparison.OrdinalIgnoreCase))
            return IntervalRule(result, history.GetMaxAgentDate("DTaP", "DPT", "Pneu"), EligibilityCriteriaCatalog.SixMonthMinimumIntervalDays, "4-month", clinic);
        if (vaccineType.Contains("4 Month", StringComparison.OrdinalIgnoreCase))
            return IntervalRule(result, history.GetMaxAgentDate("DTaP", "DPT", "Pneu", "Rota"), EligibilityCriteriaCatalog.FourMonthMinimumIntervalDays, "2-month", clinic);
        if (vaccineType.Contains("2 Month", StringComparison.OrdinalIgnoreCase))
            return AgeRule(result, clinic >= dob.AddMonths(2), "2 months", dob, clinic);
        if (vaccineType.Contains("Preschool", StringComparison.OrdinalIgnoreCase))
            return AgeRule(result, clinic >= dob.AddYears(4), "4 years", dob, clinic);
        if (vaccineType.Contains("Other", StringComparison.OrdinalIgnoreCase) || vaccineType.Contains("Autre", StringComparison.OrdinalIgnoreCase))
            return history.Records.Count > 0
                ? Eligible(result, $"Other/Autre validated OK. Prior vaccination history confirmed in PHIS ({history.Records.Count} dose(s) found).")
                : Ineligible(result, "Other/Autre KO. Appointment requires prior vaccination history in PHIS; 0 previous doses found.");

        return ManualReview(result, $"Unrecognized appointment category '{vaccineType}'. Manual review required.");
    }

    public static decimal CalculateAgeMonths(DateTime dateOfBirth, DateTime clinicDate)
    {
        DateTime dob = dateOfBirth.Date;
        DateTime clinic = clinicDate.Date;
        int completedMonths = ((clinic.Year - dob.Year) * 12) + clinic.Month - dob.Month;
        if (clinic.Day < dob.Day) completedMonths--;

        DateTime intervalStart = dob.AddMonths(completedMonths);
        DateTime intervalEnd = intervalStart.AddMonths(1);
        decimal fraction = (decimal)(clinic - intervalStart).TotalDays / (decimal)(intervalEnd - intervalStart).TotalDays;
        return completedMonths + fraction;
    }

    private static EligibilityEvaluationResult EvaluateEts(EligibilityEvaluationResult result, DateTime dob, DateTime clinic)
    {
        string age = FormatAgeMonths(result.AgeMonths);
        bool eligible = clinic >= dob.AddMonths(18) && clinic < dob.AddMonths(24);
        return eligible
            ? Eligible(result, $"HTA (ETS) Assessment criteria OK. Client age is {age}m.")
            : Ineligible(result, $"HTA (ETS) Assessment age criteria KO. Client age is {age}m (must be >= 18m and < 24m).");
    }

    private static EligibilityEvaluationResult EvaluateRespiratory(EligibilityEvaluationResult result, ClientImmunizationHistory? history, DateTime dob, DateTime clinic, string category, string? booking)
    {
        bool combo = string.Equals(category, VaccineTypeNormalizer.CovidAndInfluenza, StringComparison.OrdinalIgnoreCase);
        bool secondDose = !combo && Regex.IsMatch(booking ?? string.Empty, @"\b(?:2nd|2e|2ème|2eme|second(?:e)?)\s+dose\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!combo)
        {
            bool flu = string.Equals(category, VaccineTypeNormalizer.Influenza, StringComparison.OrdinalIgnoreCase);
            var component = EvaluateRespiratoryComponent(history, dob, clinic, result.AgeMonths!.Value, flu, secondDose);
            return Set(result, component.Status, component.Reason);
        }

        var influenza = EvaluateRespiratoryComponent(history, dob, clinic, result.AgeMonths!.Value, true, false);
        var covid = EvaluateRespiratoryComponent(history, dob, clinic, result.AgeMonths!.Value, false, false);
        EligibilityStatus status = influenza.Status == EligibilityStatus.Eligible && covid.Status == EligibilityStatus.Eligible
            ? EligibilityStatus.Eligible
            : influenza.Status == EligibilityStatus.Ineligible && covid.Status == EligibilityStatus.Ineligible
                ? EligibilityStatus.Ineligible : EligibilityStatus.ManualReview;
        return Set(result, status, $"Influenza: {influenza.Reason} | COVID-19: {covid.Reason}");
    }

    private static (EligibilityStatus Status, string Reason) EvaluateRespiratoryComponent(
        ClientImmunizationHistory? history, DateTime dob, DateTime clinic, decimal ageMonths, bool flu, bool secondDose)
    {
        string name = flu ? "Influenza" : "COVID-19";
        if (clinic < dob.AddMonths(6))
            return (EligibilityStatus.Ineligible, $"Age is {ageMonths.ToString("F1", CultureInfo.InvariantCulture)} months; below the 6-month {name} clinic screening boundary.");
        if (clinic >= dob.AddYears(5))
            return (EligibilityStatus.Ineligible, $"Age is {(ageMonths / 12m).ToString("F1", CultureInfo.InvariantCulture)} years; at or above the 5-year {name} clinic screening boundary.");

        string[] prefixes = flu ? ["Influenza"] : ["COVID", "SARS-CoV-2"];
        DateTime? latest = history?.Records
            .Where(item => prefixes.Any(prefix => item.Agent.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(item => (DateTime?)item.AdministeredDate.Date).Max();
        if (!latest.HasValue)
            return secondDose
                ? (EligibilityStatus.ManualReview, $"Booked for a second {name} dose, but no prior {name} dose is recorded in PHIS. Review the booking and immunization history before vaccination.")
                : (EligibilityStatus.Eligible, $"No prior {name} dose recorded in PHIS; within the pediatric clinic screening age window.");

        if (latest.Value > clinic)
            return (EligibilityStatus.ManualReview, $"A {name} dose is recorded on {latest:yyyy/MM/dd}, after the clinic date. Review the immunization history before vaccination.");

        int days = (clinic - latest.Value).Days;
        int referenceDays = flu ? EligibilityCriteriaCatalog.InfluenzaReferenceIntervalDays : EligibilityCriteriaCatalog.CovidReferenceIntervalDays;
        return days >= referenceDays
            ? (EligibilityStatus.Eligible, $"{name}: Eligible under the clinic interval rule. Prior dose was recorded on {latest:yyyy/MM/dd}, {days} days before the clinic date (>= {referenceDays} days required).")
            : (EligibilityStatus.Ineligible, $"{name}: Ineligible under the clinic interval rule. Prior dose was recorded on {latest:yyyy/MM/dd}, {days} days before the clinic date (>= {referenceDays} days required).");
    }

    private static EligibilityEvaluationResult Evaluate18Month(EligibilityEvaluationResult result, ClientImmunizationHistory? history, DateTime dob, DateTime clinic)
    {
        string age = FormatAgeMonths(result.AgeMonths);
        if (clinic < dob.AddMonths(18))
            return Ineligible(result, $"18m Appointment age criteria KO. Client age is {age}m (must be >= 18m).");

        DateTime? priorDose = history.GetMaxAgentDate("MMR", "MMRV");
        if (!priorDose.HasValue)
            return Eligible(result, $"18m Appointment criteria OK. Age = {age}m (>= 18m). No prior MMRV dose found in PHIS history.");

        int interval = (clinic - priorDose.Value.Date).Days;
        return interval > EligibilityCriteriaCatalog.MmrMmrvMinimumIntervalDays
            ? Eligible(result, $"18m Appointment criteria OK. Age = {age}m (>= 18m). Previous MMRV = {priorDose:yyyy-MM-dd}, interval > 6 months.")
            : Ineligible(result, $"18m Appointment dose interval KO. Previous MMRV = {priorDose:yyyy-MM-dd}, Clinic Date = {clinic:yyyy-MM-dd}. Interval <= 6 months.");
    }

    private static EligibilityEvaluationResult EvaluateEtsPlus(EligibilityEvaluationResult result, ClientImmunizationHistory? history, DateTime dob, DateTime clinic, string? catalogItem)
    {
        string age = result.AgeMonths!.Value.ToString("F1", CultureInfo.InvariantCulture);
        if (clinic < dob.AddMonths(18))
            return Ineligible(result, $"Client is {age} months old. Ineligible for ETS/ETS+ (Minimum age boundary is 18.0 months).");
        if (clinic >= dob.AddMonths(24))
            return Ineligible(result, $"Client is {age} months old. Ineligible for ETS/ETS+ (Maximum age boundary is 24.0 months).");

        bool bookedAsEts = CsvImporterService.IsAssessmentOnlyHtaCatalogItem(catalogItem);
        string evaluated = bookedAsEts ? "Booked as ETS; evaluated as ETS+ (HTA + Vaccine)." : "Booked as ETS+; evaluated as ETS+ (HTA + Vaccine).";
        string validated = bookedAsEts ? "Booked as ETS; validated as ETS+ (HTA + Vaccine)." : "Booked as ETS+; validated as ETS+ (HTA + Vaccine).";

        DateTime? priorDose = history.GetMaxAgentDate("MMR", "MMRV");
        if (!priorDose.HasValue)
            return Eligible(result, $"{validated} Within the 18–24 month age window and no prior MMR/MMRV history recorded. Eligible for HTA and vaccination.");

        if (priorDose.Value.Date > clinic)
            return ManualReview(result, $"{evaluated} An MMR/MMRV dose is recorded on {priorDose:yyyy-MM-dd}, after the clinic date. Review the immunization history before vaccination; the Healthy Toddler Assessment may proceed.");

        if (priorDose.Value.Date >= dob.AddMonths(18))
            return ManualReview(result, $"{evaluated} An MMR/MMRV dose was already administered on {priorDose:yyyy-MM-dd} (at or after 18 months). The 18-month vaccination may already have been given. Review client history before vaccination; proceed with Healthy Toddler Assessment.");

        int interval = (clinic - priorDose.Value.Date).Days;
        return interval > EligibilityCriteriaCatalog.MmrMmrvMinimumIntervalDays
            ? Eligible(result, $"{validated} Interval is {interval} days since previous MMR/MMRV on {priorDose:yyyy-MM-dd} (> 180 days required). Eligible for HTA and vaccination.")
            : ManualReview(result, $"{evaluated} Latest MMR/MMRV was administered {interval} days ago on {priorDose:yyyy-MM-dd} (> 180 days required). Vaccine is deferred; proceed with Healthy Toddler Assessment only.");
    }

    private static EligibilityEvaluationResult AgeRule(EligibilityEvaluationResult result, bool eligible, string requiredAge, DateTime dob, DateTime clinic) =>
        eligible
            ? Eligible(result, $"DOB criteria OK. DOB = {dob:yyyy-MM-dd}, Clinic Date = {clinic:yyyy-MM-dd}; minimum age {requiredAge} reached.")
            : Ineligible(result, $"DOB criteria KO. DOB = {dob:yyyy-MM-dd}, Clinic Date = {clinic:yyyy-MM-dd}; client is under {requiredAge}.");

    private static EligibilityEvaluationResult IntervalRule(EligibilityEvaluationResult result, DateTime? priorDose, int minimumDays, string priorDoseName, DateTime clinic, string prefix = "")
    {
        if (!priorDose.HasValue)
            return Eligible(result, $"{prefix}Previous {priorDoseName} dose not found in PHIS history; eligible for appointment.");
        int interval = (clinic - priorDose.Value.Date).Days;
        return interval >= minimumDays
            ? Eligible(result, $"{prefix}Dose interval OK. Previous dose = {priorDose:yyyy-MM-dd}, Clinic Date = {clinic:yyyy-MM-dd}. Interval = {interval} days (>= {minimumDays} days required).")
            : Ineligible(result, $"{prefix}Dose interval KO. Previous dose = {priorDose:yyyy-MM-dd}, Clinic Date = {clinic:yyyy-MM-dd}. Interval = {interval} days (< {minimumDays} days required).");
    }

    private static EligibilityEvaluationResult Eligible(EligibilityEvaluationResult result, string reason) => Set(result, EligibilityStatus.Eligible, reason);
    private static EligibilityEvaluationResult Ineligible(EligibilityEvaluationResult result, string reason) => Set(result, EligibilityStatus.Ineligible, reason);
    private static EligibilityEvaluationResult ManualReview(EligibilityEvaluationResult result, string reason) => Set(result, EligibilityStatus.ManualReview, reason);
    private static EligibilityEvaluationResult Set(EligibilityEvaluationResult result, EligibilityStatus status, string reason)
    {
        result.Status = status;
        result.EvaluationReason = reason;
        return result;
    }

    private static string FormatAgeMonths(decimal? ageMonths) => ageMonths?.ToString("0.#", CultureInfo.InvariantCulture) ?? "unknown";

    private static bool IsEtsPlus(string value) =>
        value.Contains("ETS+", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ETS Plus", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ETS +", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ETS/", StringComparison.OrdinalIgnoreCase);

    private static bool IsEts(string value) =>
        string.Equals(value, "ETS", StringComparison.OrdinalIgnoreCase);

    private static bool IsHtaEtsPlus(string value) =>
        string.Equals(value, "18 Month Appointment with Assessment (HTA)", StringComparison.OrdinalIgnoreCase);

    private static bool IsHtaEts(string value) =>
        string.Equals(value, "Assessment (HTA) Appointment", StringComparison.OrdinalIgnoreCase);

    private static bool IsEtsUnknown(string value) =>
        string.Equals(value, "ETS Unknown", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmbiguousEts(string value) =>
        value.StartsWith("ETS", StringComparison.OrdinalIgnoreCase);

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] formats = ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-M-d", "d/M/yyyy", "M/d/yyyy"];
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime exact)
            ? exact.Date
            : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsed) ? parsed.Date : null;
    }
}

public static class ClientImmunizationHistoryExtensions
{
    public static DateTime? GetMaxAgentDate(this ClientImmunizationHistory? history, params string[] agentKeywords)
    {
        if (history?.Records.Count is not > 0) return null;
        return history.Records
            .Where(record => agentKeywords.Any(keyword => record.Agent.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Select(record => (DateTime?)record.AdministeredDate.Date)
            .Max();
    }
}
