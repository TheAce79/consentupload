using ConsentSyncCore.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace CsvProcessing
{
    /// <summary>
    /// CsvHelper mapping for StudentRecord.
    /// Each .Name() call accepts multiple aliases so the map works for both
    /// English-header CSVs (Lewisville) and French-header CSVs (Antonine-Maillet)
    /// without requiring appsettings.json changes per deployment.
    /// </summary>
    public sealed class StudentRecordMap : ClassMap<StudentRecord>
    {
        public StudentRecordMap()
        {
            // ── Bilingual core columns ────────────────────────────────────────
            Map(m => m.LastName).Name("Last Name", "Nom de famille", "Nom");
            Map(m => m.FirstName).Name("First Name", "Prénom", "Prenom");
            Map(m => m.DateOfBirth).Name("Date of Birth", "Date de naissance", "DOB");
            Map(m => m.MedicareNumber).Name("Medicare Number", "No d'assurance-maladie", "Numéro d'assurance maladie", "No assurance maladie");
            Map(m => m.ConsentStatus).Name("Consent Status", "Statut de consentement");
            Map(m => m.School).Name("School", "École", "Ecole");
            Map(m => m.Grade).Name("Grade", "Année", "Annee", "Niveau");

            // ── Vaccine columns ───────────────────────────────────────────────
            Map(m => m.Tdap).Name("Tdap");
            Map(m => m.HPV).Name("HPV");

            // ── Phase 1 tracking columns (written by ProcessRawCsv) ───────────
            Map(m => m.ClientId).Name("ClientId");
            Map(m => m.ClientIdStatus).Name("ClientIdStatus")
                .TypeConverter<ClientIdStatusConverter>();
            Map(m => m.BestMatch).Name("BestMatch").Optional();

            // ── Duplicate tracking columns ────────────────────────────────────
            Map(m => m.IsFileRoseDefault).Name("IsFileRoseDefault")
                .TypeConverter<SafeBooleanConverter>();

            Map(m => m.IsDuplicate).Name("IsDuplicate")
                .TypeConverter<SafeBooleanConverter>()
                .Optional();

            Map(m => m.DuplicateResolved).Name("DuplicateResolved")
                .TypeConverter<SafeBooleanConverter>()
                .Optional();

            // ── Scanned PDF columns ───────────────────────────────────────────
            Map(m => m.IsScanPdf).Name("IsScanPdf")
                .TypeConverter<SafeBooleanConverter>()
                .Optional();

            Map(m => m.PdfName).Name("PdfName")
                .Optional();

            Map(m => m.IsScanPdfReady).Name("IsScanPdfReady")
                .TypeConverter<SafeBooleanConverter>()
                .Optional();
        }
    }

    /// <summary>
    /// Safe boolean converter that handles empty/null values
    /// </summary>
    public class SafeBooleanConverter : CsvHelper.TypeConversion.DefaultTypeConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "y" => true,
                _ => false
            };
        }

        public override string ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
            => value is bool b ? b.ToString().ToLowerInvariant() : "false";
    }

    /// <summary>
    /// Custom converter for ClientIdStatus enum
    /// </summary>
    public class ClientIdStatusConverter : CsvHelper.TypeConversion.DefaultTypeConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text)) return ClientIdStatus.NotProcessed;
            return int.TryParse(text, out int value) ? (ClientIdStatus)value : ClientIdStatus.NotProcessed;
        }

        public override string ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
            => value is ClientIdStatus status ? ((int)status).ToString() : "0";
    }
}