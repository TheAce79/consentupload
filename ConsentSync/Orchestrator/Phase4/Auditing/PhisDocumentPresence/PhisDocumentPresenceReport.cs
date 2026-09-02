using ConsentSyncCore.Services.Configuration;
using System.Text;

namespace Orchestrator.Phase4.Auditing.PhisDocumentPresence;

public static class PhisDocumentPresenceReport
{
    public static void Commit(string reportPath, PhisDocumentPresenceVerificationResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        ArgumentNullException.ThrowIfNull(result);
        PhisDocumentPresenceReportWriter.Commit(reportPath, result);
    }
}

internal static class PhisDocumentPresenceReportWriter
{
    private const string Heading = "PHIS DOCUMENT PRESENCE VERIFICATION";

    internal static void Commit(string reportPath, PhisDocumentPresenceVerificationResult result)
    {
        Encoding encoding = EncodingConfigurationService.GetPriorityEncoding();
        string existing = File.ReadAllText(reportPath, encoding);
        string[] lines = existing.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int heading = Array.FindLastIndex(lines, line => line == Heading);
        string prefix = heading >= 0 ? string.Join(Environment.NewLine, lines.Take(heading)).TrimEnd() : existing.TrimEnd();
        string content = string.IsNullOrEmpty(prefix) ? BuildSection(result) : prefix + Environment.NewLine + Environment.NewLine + BuildSection(result);
        string temporaryPath = reportPath + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporaryPath, false, encoding)) { writer.Write(content); writer.Flush(); }
            File.Move(temporaryPath, reportPath, true);
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    private static string BuildSection(PhisDocumentPresenceVerificationResult result)
    {
        var writer = new StringBuilder();
        writer.AppendLine(Heading).AppendLine(new string('=', 60)).AppendLine();
        writer.AppendLine($"Expected documents                    : {result.ExpectedDocuments}");
        writer.AppendLine($"Accepted exceptions excluded          : {result.Plan.ExcludedAcceptedExceptions}");
        writer.AppendLine($"Confirmed present on PHIS             : {result.FoundDocuments}");
        writer.AppendLine($"Missing from PHIS                     : {result.MissingDocuments}");
        writer.AppendLine($"Could not verify                      : {result.VerificationErrors}");
        writer.AppendLine().AppendLine("RESULT").AppendLine(new string('-', 60));
        if (result.ExpectedDocuments == 0) writer.AppendLine("NO ELIGIBLE DOCUMENTS WERE AVAILABLE FOR PHIS VERIFICATION.").AppendLine().AppendLine("No PHIS session was opened and no document operation was performed.");
        else if (result.AllExpectedDocumentsPresent) writer.AppendLine("ALL EXPECTED DOCUMENTS WERE CONFIRMED PRESENT ON PHIS.");
        else if (result.VerificationErrors > 0) writer.AppendLine("INCOMPLETE - ONE OR MORE DOCUMENTS COULD NOT BE VERIFIED.");
        else writer.AppendLine("ONE OR MORE EXPECTED DOCUMENTS ARE MISSING FROM PHIS.");
        writer.AppendLine().AppendLine("The expected PHIS document list was obtained from eligible rows in Verification_Upload.csv. Local archive folders were not used as the sole verification scope because a document may already have existed on PHIS before Phase 3 attempted a new upload.");
        return writer.ToString().TrimEnd();
    }
}
