namespace ConsentSyncCore.Models;

/// <summary>A clinically focused row prepared from the filtered eligibility preview.</summary>
public sealed record ClinicalCohortExportRow(
    string ClientId,
    string FullName,
    string? ClinicDate,
    string? DateOfBirth,
    string? Timeslot,
    string? VaccineType,
    EligibilityStatus? Status,
    string? EvaluationReason);
