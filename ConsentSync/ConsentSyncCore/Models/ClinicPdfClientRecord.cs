namespace ConsentSyncCore.Models;

public class ClinicPdfClientRecord
{
    public string? ClientId { get; set; }

    public string FullName { get; set; } = string.Empty;

    // Reserved for a future phase; do not infer name components from FullName.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? MiddleName { get; set; }

    /// <summary>Date of birth in yyyy/MM/dd format.</summary>
    public string DateOfBirth { get; set; } = string.Empty;

    public string? Medicare { get; set; }

    public string VaccineType { get; set; } = "Autre";

    public ClientIdStatus ClientIdStatus { get; set; } = ClientIdStatus.NotProcessed;

    /// <summary>Reason a PHIS resolution needs manual review.</summary>
    public string? ErrorDetails { get; set; }

    /// <summary>Highest-scoring PHIS candidate hint for manual review.</summary>
    public string? BestMatch { get; set; }

    /// <summary>Email copied from the resolved PHIS client preview when the CSV did not supply one.</summary>
    public string? Email { get; set; }

    /// <summary>Telephone supplied by the CSV for preview identity verification.</summary>
    public string? Phone { get; set; }

    // Raw AbleAssess values retained for downstream eligibility and audit work.
    public string? BookingId { get; set; }
    public string? ClinicName { get; set; }
    public string? ClinicDate { get; set; }
    public string? AppointmentType { get; set; }
    public string? CatalogItem { get; set; }
    public string? Timeslot { get; set; }
    public string? Comment { get; set; }
    public string? SdcId { get; set; }
    public string? PreferredLanguage { get; set; }
}
