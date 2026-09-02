using ConsentSyncCore.Services.Phis;
using Orchestrator.Phase4.Auditing.PhisDocumentPresence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Orchestrator.Tests;

public sealed class PhisDocumentPresenceVerificationTests
{
    [Fact]
    public void Prepare_preserves_leading_zero_client_ids_and_collapses_exact_targets()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "Verification_Upload.csv");
            File.WriteAllText(path, Header + "0012,Packet A,false,HPV,1,1,0\n0012,Packet_A,false,hpv,1,1,0\n0012,Rose-1,true,,2,3,0\n", Encoding.UTF8);
            PhisDocumentPresenceVerificationPlan plan = PhisDocumentPresenceVerificationService.Prepare(path);
            Assert.Single(plan.Targets);
            Assert.Equal("0012", plan.Targets[0].ClientId);
            Assert.Equal(1, plan.DuplicateTargetsCollapsed);
            Assert.Equal(1, plan.ExcludedAcceptedExceptions);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Prepare_blocks_all_non_accepted_status_combinations()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "Verification_Upload.csv");
            File.WriteAllText(path, Header + "1,Packet,false,HPV,0,1,0\n2,Packet,false,HPV,2,1,0\n3,Packet,false,HPV,1,3,0\n", Encoding.UTF8);
            PhisDocumentPresencePreconditionException ex = Assert.Throws<PhisDocumentPresencePreconditionException>(() => PhisDocumentPresenceVerificationService.Prepare(path));
            Assert.Equal(3, ex.InvalidStatusRows);
            Assert.NotEmpty(ex.Examples);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Prepare_adds_missing_phis_verification_status_and_treats_old_rows_as_not_verified()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "Verification_Upload.csv");
            File.WriteAllText(path, LegacyHeader + "0012,Packet,false,HPV,1,1\n", Encoding.UTF8);

            PhisDocumentPresenceVerificationPlan plan = PhisDocumentPresenceVerificationService.Prepare(path);

            Assert.Single(plan.Targets);
            string[] lines = File.ReadAllLines(path);
            Assert.EndsWith(",PhisVerificationStatus", lines[0]);
            Assert.EndsWith(",0", lines[1]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Prepare_skips_ok_rows_and_retries_not_verified_or_ko_rows_with_batch_limit()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "Verification_Upload.csv");
            File.WriteAllText(path, Header + "1,Already,false,HPV,1,1,1\n2,New,false,HPV,1,1,0\n3,Retry,false,HPV,1,1,2\n", Encoding.UTF8);

            PhisDocumentPresenceVerificationPlan plan = PhisDocumentPresenceVerificationService.Prepare(path, batchSize: 1);

            Assert.Single(plan.Targets);
            Assert.Equal("New", plan.Targets[0].DocumentTitle);
            Assert.Equal(1, plan.AlreadyVerifiedRows);
            Assert.Equal(2, plan.PendingRows);
            Assert.True(plan.BatchLimitReached);
            Assert.Equal(1, plan.RemainingAfterBatch);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Verify_processes_targets_in_read_only_gateway_order()
    {
        var gateway = new FakeGateway();
        var plan = new PhisDocumentPresenceVerificationPlan { Targets = [new PhisDocumentPresenceTarget { ClientId = "001", DocumentTitle = "Consent", PhisAntigen = "HPV" }] };
        var result = await new PhisDocumentPresenceVerificationService(gateway).VerifyAsync(plan);
        Assert.True(result.AllExpectedDocumentsPresent);
        Assert.Equal(new[] { "Ensure", "Context:001", "Consent:HPV", "FindConsent:Consent", "Return", "Ensure" }, gateway.Calls);
    }

    [Fact]
    public async Task Verify_updates_phis_verification_status_after_each_result()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "Verification_Upload.csv");
            File.WriteAllText(path, Header + "1,Found,false,HPV,1,1,0\n2,Missing,false,HPV,1,1,0\n", Encoding.UTF8);
            PhisDocumentPresenceVerificationPlan plan = PhisDocumentPresenceVerificationService.Prepare(path);
            var gateway = new FakeGateway();
            gateway.MissingTitles.Add("Missing");

            PhisDocumentPresenceVerificationResult result = await new PhisDocumentPresenceVerificationService(gateway).VerifyAsync(plan);

            Assert.Equal(1, result.FoundDocuments);
            Assert.Equal(1, result.MissingDocuments);
            string text = File.ReadAllText(path);
            Assert.Contains("1,Found,false,HPV,1,1,1", text);
            Assert.Contains("2,Missing,false,HPV,1,1,2", text);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Commit_replaces_only_the_final_exact_presence_section()
    {
        string root = CreateRoot();
        try
        {
            string report = Path.Combine(root, "Document_Reconciliation_Audit.txt");
            File.WriteAllText(report, "Other PHIS DOCUMENT note\n\nPHIS DOCUMENT PRESENCE VERIFICATION\nold", Encoding.UTF8);
            PhisDocumentPresenceReport.Commit(report, new PhisDocumentPresenceVerificationResult { Plan = new PhisDocumentPresenceVerificationPlan() });
            string text = File.ReadAllText(report);
            Assert.Contains("Other PHIS DOCUMENT note", text);
            Assert.DoesNotContain(Environment.NewLine + "old", text);
            Assert.Equal(1, text.Split("PHIS DOCUMENT PRESENCE VERIFICATION").Length - 1);
        }
        finally { Directory.Delete(root, true); }
    }

    private const string LegacyHeader = "ClientID,Document Title,IsFeuilleRose,PhisAntigen,VerifStatus,VerifClientIdStatus\n";
    private const string Header = "ClientID,Document Title,IsFeuilleRose,PhisAntigen,VerifStatus,VerifClientIdStatus,PhisVerificationStatus\n";
    private static string CreateRoot() { string root = Path.Combine(Path.GetTempPath(), "ConsentSync-Presence-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }

    private sealed class FakeGateway : IPhisDocumentPresenceGateway
    {
        public List<string> Calls { get; } = [];
        public HashSet<string> MissingTitles { get; } = [];
        public bool EnsureSessionValid() { Calls.Add("Ensure"); return true; }
        public Task<bool> SetClientContextAsync(string clientId) { Calls.Add("Context:" + clientId); return Task.FromResult(true); }
        public Task<bool> OpenConsentDocumentListAsync(string phisAntigen) { Calls.Add("Consent:" + phisAntigen); return Task.FromResult(true); }
        public Task<bool> OpenFileRoseDocumentListAsync() { Calls.Add("FileRose"); return Task.FromResult(true); }
        public Task<PhisDocumentLookupResult> FindConsentDocumentAsync(string documentTitle) { Calls.Add("FindConsent:" + documentTitle); return Task.FromResult(MissingTitles.Contains(documentTitle) ? PhisDocumentLookupResult.NotFound() : PhisDocumentLookupResult.Found(documentTitle)); }
        public Task<PhisDocumentLookupResult> FindFileRoseDocumentAsync(string documentTitle) { Calls.Add("FindFileRose:" + documentTitle); return Task.FromResult(MissingTitles.Contains(documentTitle) ? PhisDocumentLookupResult.NotFound() : PhisDocumentLookupResult.Found(documentTitle)); }
        public Task<bool> ReturnToSearchAsync() { Calls.Add("Return"); return Task.FromResult(true); }
    }
}
