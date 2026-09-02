using Orchestrator.Phase4.Auditing.DocumentReconciliation;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Orchestrator.Tests;

public sealed class DocumentReconciliationAuditTests
{
    [Fact]
    public void ResolveArchivePath_stays_inside_the_exact_archive_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "3 Consent Archive");
        string result = DocumentReconciliationAuditService.ResolveArchivePath(root, "123_consentHPV_2026");

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "123_consentHPV_2026.pdf"), result);
        Assert.Throws<InvalidDataException>(() => DocumentReconciliationAuditService.ResolveArchivePath(root, "..\\3 Consent Archive - Old\\packet"));
        Assert.Throws<InvalidDataException>(() => DocumentReconciliationAuditService.ResolveArchivePath(root, "C:\\outside.pdf"));
        Assert.Throws<InvalidDataException>(() => DocumentReconciliationAuditService.ResolveArchivePath(root, "packet:stream"));
    }

    [Theory]
    [InlineData(true, 0, 0, false, 0, 0, ConsentPageOrigin.Blank)]
    [InlineData(false, 0, 0, true, .81, .0, ConsentPageOrigin.ManualConsent)]
    [InlineData(false, 140, 10, true, .01, .01, ConsentPageOrigin.DigitalConsent)]
    [InlineData(false, 0, 0, false, 0, 0, ConsentPageOrigin.Unknown)]
    public void Classify_uses_source_geometry_before_native_text(bool blank, int characters, int words, bool reliableGeometry, double largest, double union, ConsentPageOrigin expected)
    {
        var evidence = new PdfPageEvidence
        {
            IsVisuallyBlank = blank,
            NativeTextCharacterCount = characters,
            NativeWordCount = words,
            HasReliableRasterGeometry = reliableGeometry,
            LargestRasterCoverageRatio = largest,
            RasterUnionCoverageRatio = union
        };

        Assert.Equal(expected, DocumentReconciliationAuditService.Classify(evidence));
    }

    [Fact]
    public void Result_keeps_review_and_completeness_separate()
    {
        var result = new DocumentReconciliationAuditResult
        {
            Issues = [new DocumentReconciliationIssue
            {
                Code = DocumentReconciliationIssueCodes.BlankConsentPage,
                Severity = DocumentReconciliationIssueSeverity.Warning,
                Message = "Blank page.",
                AffectsCompleteness = false
            }]
        };

        Assert.True(result.HasReviewIssues);
        Assert.True(result.CountsAreComplete);
    }

    [Fact]
    public void Status_three_only_writes_a_zero_count_report_without_archive_folders()
    {
        string root = Path.Combine(Path.GetTempPath(), "ConsentSync-Audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string csv = Path.Combine(root, "Verification_Upload.csv");
            File.WriteAllText(csv, "ClientID,Document Title,IsFeuilleRose,PhisAntigen,VerifClientIdStatus\n1,packet,false,HPV-9,3\n", Encoding.UTF8);
            var service = new DocumentReconciliationAuditService(new ThrowingInspector());

            DocumentReconciliationAuditResult result = service.ExecuteAudit(new DocumentReconciliationAuditService.AuditPaths(csv, Path.Combine(root, "missing-consent"), Path.Combine(root, "missing-rose"), Path.Combine(root, "Document_Reconciliation_Audit.txt")));

            Assert.Equal(0, result.ConsentUploadRows);
            Assert.Equal(0, result.FileRoseUploadRows);
            Assert.True(result.CountsAreComplete);
            Assert.True(File.Exists(result.OutputPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Blocking_status_fails_before_any_pdf_inspection()
    {
        string root = Path.Combine(Path.GetTempPath(), "ConsentSync-Audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string csv = Path.Combine(root, "Verification_Upload.csv");
            File.WriteAllText(csv, "ClientID,Document Title,IsFeuilleRose,PhisAntigen,VerifClientIdStatus\n1,packet,false,HPV-9,0\n", Encoding.UTF8);
            var service = new DocumentReconciliationAuditService(new ThrowingInspector());

            Assert.Throws<InvalidDataException>(() => service.ExecuteAudit(new DocumentReconciliationAuditService.AuditPaths(csv, Path.Combine(root, "missing-consent"), Path.Combine(root, "missing-rose"), Path.Combine(root, "report.txt"))));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("7", "Grade7")]
    [InlineData("9", "Grade9")]
    public void Vaccine_grade_key_is_explicitly_normalized(string grade, string expected)
    {
        Assert.Equal(expected, DocumentReconciliationAuditService.GetVaccineGradeKey(grade));
    }

    [Fact]
    public void Grade_seven_identical_vaccine_copies_count_one_physical_consent()
    {
        using var fixture = new AuditFixture();
        fixture.Write("123_hpv.pdf", "same-packet");
        fixture.Write("123_tdap.pdf", "same-packet");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,1\n123,123_tdap,false,Tetanus (T),1\n");

        Assert.Equal(2, result.ConsentUploadRows);
        Assert.Equal(1, result.UniqueConsentClientIds);
        Assert.Equal(1, result.ExpectedPhysicalConsentClients);
        Assert.Equal(1, result.TrustedConsentClientsCounted);
        Assert.Equal(1, result.VaccineSpecificConsentRowsCollapsed);
        Assert.Equal(1, result.IdenticalConsentArchiveCopiesCollapsed);
        Assert.Equal(1, result.ConsentPages);
        Assert.Equal(1, result.DigitalConsentPages);
    }

    [Fact]
    public void Status_three_exception_is_validation_only()
    {
        using var fixture = new AuditFixture();
        fixture.Write("123_hpv.pdf", "packet");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,1\n123,123_tdap,false,Tetanus (T),3\n");

        Assert.Equal(1, result.TrustedConsentClientsCounted);
        Assert.Equal(1, fixture.InspectionCalls);
        Assert.DoesNotContain(result.Issues, issue => issue.Code == DocumentReconciliationIssueCodes.MissingConfiguredConsentAntigen);
    }

    [Fact]
    public void Mismatched_vaccine_copies_remain_a_diagnostic_warning_without_changing_physical_readiness()
    {
        using var fixture = new AuditFixture();
        fixture.Write("123_hpv.pdf", "packet-one");
        fixture.Write("123_tdap.pdf", "packet-two");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,1\n123,123_tdap,false,Tetanus (T),1\n");

        Assert.Equal(1, result.ExpectedPhysicalConsentClients);
        Assert.Equal(1, result.TrustedConsentClientsCounted);
        Assert.Equal(1, result.ConsentVaccineCopyMismatchGroups);
        Assert.Equal(1, result.ConsentPages);
        Assert.True(result.CountsAreComplete);
        Assert.True(result.HasIntegrityWarnings);
        Assert.True(result.OverallPhysicalReconciliationReady);
        Assert.Contains(result.Issues, issue => issue.Code == DocumentReconciliationIssueCodes.ConsentVaccineCopyContentMismatch && issue.Severity == DocumentReconciliationIssueSeverity.Warning);

        string report = File.ReadAllText(result.OutputPath);
        Assert.Contains(DocumentReconciliationIssueCodes.ConsentVaccineCopyContentMismatch, report);
        Assert.DoesNotContain("Technical archive integrity", report);
    }

    [Fact]
    public void Multiple_filerose_paths_do_not_increase_trusted_document_count()
    {
        using var fixture = new AuditFixture();
        fixture.Write("123_hpv.pdf", "packet");
        fixture.WriteRose("123_rose_a.pdf", "one");
        fixture.WriteRose("123_rose_b.pdf", "two");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,1\n123,123_rose_a,true,,1\n123,123_rose_b,true,,1\n");

        Assert.Equal(0, result.FileRosePdfDocuments);
        Assert.Equal(0, result.FileRosePages);
        Assert.Equal(1, result.MultipleFileRoseDocumentClientGroups);
        Assert.False(result.CountsAreComplete);
    }

    [Fact]
    public void Entered_source_counts_reconcile_manual_and_filerose_forms_independently()
    {
        using var fixture = new AuditFixture();
        fixture.Write("123_hpv.pdf", "packet");
        fixture.WriteRose("123_rose.pdf", "rose");

        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,1\n123,123_rose,true,,1\n",
            new DocumentReconciliationAuditRequest
            {
                OriginalDigitalConsentSubmissions = 1,
                ExpectedManualConsentForms = 0,
                ExpectedFileRoseForms = 2
            });

        Assert.Equal(1, result.UniqueDigitalConsentClients);
        Assert.Equal(0, result.AdditionalDigitalSourceSubmissions);
        Assert.Equal(0, result.DetectedManualConsentForms);
        Assert.Equal(2, result.DetectedFileRoseForms);
        Assert.True(result.OverallPhysicalReconciliationReady);
    }

    [Fact]
    public void Source_count_below_unique_clients_is_a_warning_not_a_physical_failure()
    {
        using var fixture = new AuditFixture();
        fixture.Write("123_hpv.pdf", "packet");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,1\n",
            new DocumentReconciliationAuditRequest { OriginalDigitalConsentSubmissions = 0 });

        Assert.Null(result.AdditionalDigitalSourceSubmissions);
        Assert.Contains(result.Issues, issue => issue.Code == DocumentReconciliationIssueCodes.SnbSourceCountBelowUniqueClientCount);
        Assert.True(result.PhysicalCountsAvailable);
    }

    [Fact]
    public void Two_page_filerose_is_two_physical_forms_merged_in_one_pdf()
    {
        using var fixture = new AuditFixture();
        fixture.WriteRose("123_rose.pdf", "rose");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,3\n123,123_rose,true,,1\n",
            new DocumentReconciliationAuditRequest { ExpectedFileRoseForms = 2 });

        Assert.Equal(1, result.FileRosePdfDocuments);
        Assert.Equal(2, result.DetectedFileRoseForms);
        Assert.Equal(2, result.FileRosePages);
        Assert.Equal(1, result.MultiPageFileRoseDocuments);
        Assert.Equal(1, result.AdditionalMergedFileRoseForms);
        Assert.True(result.FileRoseFormCountMatches);
    }

    [Fact]
    public void Filerose_without_a_same_batch_consent_is_untrusted()
    {
        using var fixture = new AuditFixture();
        fixture.WriteRose("123_rose.pdf", "rose");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_rose,true,,1\n",
            new DocumentReconciliationAuditRequest { ExpectedFileRoseForms = 1 });

        Assert.Equal(0, result.DetectedFileRoseForms);
        Assert.Equal(0, result.FileRosePages);
        Assert.False(result.PhysicalCountsAvailable);
        Assert.Contains(result.Issues, issue => issue.Code == DocumentReconciliationIssueCodes.FileRoseClientWithoutConsent);
        Assert.False(Assert.Single(result.FileRoseDetails).IsTrusted);
    }

    [Fact]
    public void Filerose_title_mismatch_is_a_warning_when_consent_matches()
    {
        using var fixture = new AuditFixture();
        fixture.Write("123_hpv.pdf", "packet");
        fixture.WriteRose("999_rose.pdf", "rose");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,1\n123,999_rose,true,,1\n",
            new DocumentReconciliationAuditRequest { ExpectedFileRoseForms = 2 });

        Assert.Equal(2, result.DetectedFileRoseForms);
        Assert.Contains(result.Issues, issue => issue.Code == DocumentReconciliationIssueCodes.DocumentTitleClientIdMismatch);
        Assert.True(Assert.Single(result.FileRoseDetails).IsTrusted);
    }

    [Fact]
    public void Multi_page_filerose_represents_multiple_physical_forms()
    {
        using var fixture = new AuditFixture();
        fixture.WriteRose("123_rose.pdf", "rose");
        DocumentReconciliationAuditResult result = fixture.Run(
            "123,123_hpv,false,HPV-9,3\n123,123_rose,true,,1\n",
            new DocumentReconciliationAuditRequest { ExpectedFileRoseForms = 2 });

        Assert.Equal(2, result.ExpectedFileRoseForms);
        Assert.Equal(1, result.FileRosePdfDocuments);
        Assert.Equal(2, result.DetectedFileRoseForms);
        Assert.Equal(2, result.FileRosePages);
        Assert.Equal(1, result.AdditionalMergedFileRoseForms);
        Assert.True(result.FileRoseFormCountMatches);
        Assert.Contains(result.Issues, issue => issue.Code == DocumentReconciliationIssueCodes.FileRoseMultipleFormsMerged);
        Assert.Equal(
            DocumentReconciliationIssueSeverity.Information,
            Assert.Single(result.Issues, issue => issue.Code == DocumentReconciliationIssueCodes.FileRoseMultipleFormsMerged).Severity);
        Assert.True(result.OverallPhysicalReconciliationReady);
    }

    private sealed class ThrowingInspector : IPdfAuditInspector
    {
        public PdfInspection Inspect(string path, bool includeConsentEvidence) => throw new InvalidOperationException("PDF inspection must not run.");
    }

    private sealed class AuditFixture : IDisposable, IPdfAuditInspector
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ConsentSync-Audit-" + Guid.NewGuid().ToString("N"));
        private readonly string _consent;
        private readonly string _rose;

        public AuditFixture()
        {
            _consent = Path.Combine(_root, "3 Consent Archive");
            _rose = Path.Combine(_root, "4 Rose Archive");
            Directory.CreateDirectory(_consent);
            Directory.CreateDirectory(_rose);
        }

        public int InspectionCalls { get; private set; }

        public void Write(string name, string contents) => File.WriteAllText(Path.Combine(_consent, name), contents, Encoding.UTF8);
        public void WriteRose(string name, string contents) => File.WriteAllText(Path.Combine(_rose, name), contents, Encoding.UTF8);

        public DocumentReconciliationAuditResult Run(string rows, DocumentReconciliationAuditRequest request = null)
        {
            string csv = Path.Combine(_root, "Verification_Upload.csv");
            File.WriteAllText(csv, "ClientID,Document Title,IsFeuilleRose,PhisAntigen,VerifClientIdStatus\n" + rows, Encoding.UTF8);
            var config = new DocumentReconciliationAuditService.AuditConfiguration("7", ["HPV9", "Tdap"], ["HPV-9", "Tetanus (T)"], string.Empty);
            var service = new DocumentReconciliationAuditService(this, () => config);
            return service.ExecuteAudit(request ?? new DocumentReconciliationAuditRequest(), new DocumentReconciliationAuditService.AuditPaths(csv, _consent, _rose, Path.Combine(_root, "report.txt")));
        }

        public PdfInspection Inspect(string path, bool includeConsentEvidence)
        {
            InspectionCalls++;
            int pages = includeConsentEvidence ? 1 : 2;
            var evidence = new PdfPageEvidence { NativeTextCharacterCount = 200, NativeWordCount = 40, HasReliableRasterGeometry = true };
            return new PdfInspection(pages, pages, Enumerable.Repeat(evidence, pages).ToArray());
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
