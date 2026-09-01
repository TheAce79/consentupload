namespace Orchestrator.Phase4.Auditing.ClientIdentity;

internal static class AcceptedUploadExceptionPolicy
{
    public static bool IsAcceptedException(int uploadStatus, string? remarksByMelisa) =>
        uploadStatus == 2 && !string.IsNullOrWhiteSpace(remarksByMelisa);
}
