using ConsentSyncCore.Services.Pdf;
using Xunit;

namespace Orchestrator.Tests;

public sealed class ClinicScheduleSummaryTests
{
    [Fact]
    public void Compare_ReportsScheduleChangesWithoutRemovingPriorClients()
    {
        var previous = new[] { new ClinicScheduleClient("A_2020/01/01", "A", "2020/01/01", "2 mois"), new ClinicScheduleClient("B_2020/01/02", "B", "2020/01/02", "Autre") };
        var current = new[] { previous[1], new ClinicScheduleClient("C_2020/01/03", "C", "2020/01/03", "PS") };
        var comparison = ClinicScheduleSummary.Compare(current, previous);
        Assert.Equal("C", Assert.Single(comparison.Added).FullName);
        Assert.Equal("A", Assert.Single(comparison.Removed).FullName);
        var report = ClinicScheduleSummary.Format("LIST", 1, 2, comparison, DateTime.UtcNow, ["schedule.pdf"], null, null);
        Assert.Contains("These clients remain in PHIS", report);
        Assert.Contains("FULL CURRENT CLINIC SCHEDULE", report);
    }
}
