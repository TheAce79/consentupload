using ConsentSyncCore.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace CsvProcessing
{
    /// <summary>
    /// CsvHelper mapping for StudentRecord
    /// Maps CSV column names to class properties
    /// </summary>
    public sealed class StudentRecordMap : ClassMap<StudentRecord>
    {
        public StudentRecordMap()
        {
            Map(m => m.LastName).Name("Last Name");
            Map(m => m.FirstName).Name("First Name");
            Map(m => m.School).Name("School");
            Map(m => m.Grade).Name("Grade");
            Map(m => m.DateOfBirth).Name("Date of Birth");
            Map(m => m.MedicareNumber).Name("Medicare Number");
            Map(m => m.ConsentStatus).Name("Consent Status");
            Map(m => m.Tdap).Name("Tdap");
            Map(m => m.HPV).Name("HPV");
            Map(m => m.ClientId).Name("ClientId");
            Map(m => m.IsFileRoseDefault).Name("IsFileRoseDefault")
                .TypeConverter<SafeBooleanConverter>(); // Use custom converter
            Map(m => m.ClientIdStatus).Name("ClientIdStatus")
                .TypeConverter<ClientIdStatusConverter>();
            Map(m => m.BestMatch).Name("BestMatch").Optional(); // Optional for backward compatibility
        }
    }

    /// <summary>
    /// Safe boolean converter that handles empty/null values
    /// </summary>
    public class SafeBooleanConverter : CsvHelper.TypeConversion.DefaultTypeConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            // Handle empty/null values
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Normalize the text
            text = text.Trim().ToLowerInvariant();

            // Handle common boolean representations
            return text switch
            {
                "true" or "1" or "yes" or "y" => true,
                "false" or "0" or "no" or "n" => false,
                _ => false // Default to false for any unrecognized value
            };
        }

        public override string ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
        {
            if (value is bool boolValue)
            {
                return boolValue.ToString().ToLowerInvariant();
            }

            return "false";
        }
    }

    /// <summary>
    /// Custom converter for ClientIdStatus enum
    /// </summary>
    public class ClientIdStatusConverter : CsvHelper.TypeConversion.DefaultTypeConverter
    {
        public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text)) return ClientIdStatus.NotProcessed;

            if (int.TryParse(text, out int value))
            {
                return (ClientIdStatus)value;
            }

            return ClientIdStatus.NotProcessed;
        }

        public override string ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
        {
            if (value is ClientIdStatus status)
            {
                return ((int)status).ToString();
            }

            return "0";
        }
    }



}