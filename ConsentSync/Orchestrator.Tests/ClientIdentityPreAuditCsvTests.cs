using System.Reflection;
using System.Text;
using Orchestrator.Phase4.Auditing.ClientIdentity;
using Xunit;

namespace Orchestrator.Tests;

public sealed class ClientIdentityPreAuditCsvTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ConsentSyncClientIdentityTests", Guid.NewGuid().ToString("N"));

    public ClientIdentityPreAuditCsvTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReadUploadTable_AcceptsMissingRemarksByMelisaColumn()
    {
        string path = Path.Combine(_directory, "Upload_to_PHIS.csv");
        File.WriteAllText(path, "ClientID,Last Name,First Name,VerifStatus,FailureReason\n1,Example,Student,2,UploadFailed\n", Encoding.UTF8);

        MethodInfo reader = typeof(ClientIdentityPreAuditService).GetMethod("ReadUploadTable", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? table = reader.Invoke(null, [path, Encoding.UTF8]);

        Assert.NotNull(table);
    }

    [Fact]
    public void ReadUploadTable_AcceptsEmptyRemarksByMelisaValues()
    {
        string path = Path.Combine(_directory, "Upload_with_remarks.csv");
        File.WriteAllText(path, "ClientID,Last Name,First Name,VerifStatus,FailureReason,Remarks By Melisa\n1,Example,Student,2,UploadFailed,\n", Encoding.UTF8);

        MethodInfo reader = typeof(ClientIdentityPreAuditService).GetMethod("ReadUploadTable", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? table = reader.Invoke(null, [path, Encoding.UTF8]);

        Assert.NotNull(table);
    }

    [Fact]
    public void AcceptedExceptionPolicy_RequiresNonEmptyRemarks()
    {
        Assert.False(AcceptedUploadExceptionPolicy.IsAcceptedException(2, string.Empty));
        Assert.True(AcceptedUploadExceptionPolicy.IsAcceptedException(2, "Documented exception"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
