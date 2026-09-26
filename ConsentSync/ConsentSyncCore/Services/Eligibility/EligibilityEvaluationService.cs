using System.Globalization;
using ConsentSyncCore.Models;

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
        if (history is null)
            return ManualReview(result, "No History Found: client is absent from the GNB2009 Excel history. Manual review required.");

        string vaccineType = record.Source.VaccineType?.Trim() ?? string.Empty;
        if (vaccineType.Length == 0 || string.Equals(vaccineType, "Unknown", StringComparison.OrdinalIgnoreCase))
            return ManualReview(result, "Unknown vaccine type. Manual review required.");

        DateTime dob = dateOfBirth.Value.Date;
        DateTime clinic = clinicDate.Value.Date;
        if (vaccineType.Contains("18 Month", StringComparison.OrdinalIgnoreCase))
        {
            if (clinic < dob.AddMonths(18))
                return Ineligible(result, $"Age criteria KO. Client is under 18 months on clinic date {clinic:yyyy-MM-dd}.");
            return IntervalRule(result, history.GetMaxAgentDate("MMR", "MMRV"), 180, "MMR1", clinic, "Age criteria OK (>= 18 months). ");
        }
        if (vaccineType.Contains("12 Month", StringComparison.OrdinalIgnoreCase))
            return AgeRule(result, clinic >= dob.AddYears(1), "the first birthday", dob, clinic);
        if (vaccineType.Contains("6 Month", StringComparison.OrdinalIgnoreCase))
            return IntervalRule(result, history.GetMaxAgentDate("DTaP", "DPT", "Pneu"), 56, "4-month", clinic);
        if (vaccineType.Contains("4 Month", StringComparison.OrdinalIgnoreCase))
            return IntervalRule(result, history.GetMaxAgentDate("DTaP", "DPT", "Pneu", "Rota"), 28, "2-month", clinic);
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
