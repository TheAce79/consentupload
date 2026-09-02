using ConsentSyncCore.Services.Configuration;
using Microsoft.Extensions.Configuration;
using Orchestrator.Phase4.Auditing.PhisDocumentPresence;

namespace OrchestratorUi;

public partial class UploadConsent
{
    private bool _phisPresenceVerificationRunning;

    private async void btn_VerifyDocumentsOnPhis_Click(object sender, EventArgs e)
    {
        if (_phisPresenceVerificationRunning) return;

        _phisPresenceVerificationRunning = true;
        bool verifierWasEnabled = btn_VerifyDocumentsOnPhis.Enabled;
        string verifierText = btn_VerifyDocumentsOnPhis.Text;
        btn_VerifyDocumentsOnPhis.Enabled = false;
        btn_VerifyDocumentsOnPhis.Text = "Preparing verification...";
        var capturedStates = new Dictionary<Control, bool>();

        try
        {
            var prePhase3 = ConfigurationService.GetPrePhase3Config();
            string verificationCsvPath = Path.Combine(prePhase3.OutputPath, "Verification_Upload.csv");
            string reportPath = Path.Combine(prePhase3.OutputPath, "Document_Reconciliation_Audit.txt");
            EnsureReportCanBeCommitted(reportPath);
            if (!File.Exists(verificationCsvPath)) throw new FileNotFoundException("Verification_Upload.csv was not found. Run the Client Identity Pre-Audit first.", verificationCsvPath);

            PhisDocumentPresenceVerificationPlan plan = PhisDocumentPresenceVerificationService.Prepare(verificationCsvPath);
            if (!bt_Upload.Enabled || !bt_SearchClientId.Enabled)
            {
                MessageBox.Show(this, "PHIS Document Verification cannot start while another PHIS operation is running. Wait for the current operation to complete and try again.", "PHIS Operation In Progress", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (plan.Targets.Count == 0)
            {
                PhisDocumentPresenceReport.Commit(reportPath, new PhisDocumentPresenceVerificationResult { Plan = plan });
                MessageBox.Show(this, "No eligible documents were available for PHIS verification. No PHIS session was opened.", "PHIS Verification", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            foreach (Control control in new Control[] { bt_Upload, bt_SearchClientId, btn_ExportMassImms })
            {
                capturedStates[control] = control.Enabled;
                control.Enabled = false;
            }

            IConfiguration config = ConfigurationService.GetConfiguration();
            await EnsurePhisSessionAsync(config);
            if (_phisSearchService == null || _sessionManager == null)
                throw new InvalidOperationException("The PHIS session could not be initialized.");

            btn_VerifyDocumentsOnPhis.Text = "Verifying documents...";
            var progress = new Progress<PhisDocumentPresenceProgress>(item =>
            {
                btn_VerifyDocumentsOnPhis.Text = $"Verifying {item.Current}/{item.Total}";
            });
            var service = new PhisDocumentPresenceVerificationService(new PhisDocumentPresenceGateway(_phisSearchService, _sessionManager));
            PhisDocumentPresenceVerificationResult result = await service.VerifyAsync(plan, progress);

            if (result.UnprocessedDocuments > 0)
            {
                MessageBox.Show(this, $"PHIS verification stopped with {result.UnprocessedDocuments} document(s) unprocessed. The previous report section was preserved.", "PHIS Verification Incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PhisDocumentPresenceReport.Commit(reportPath, result);
            MessageBox.Show(this, $"PHIS document verification completed.\n\nConfirmed present: {result.FoundDocuments}\nMissing: {result.MissingDocuments}\nCould not verify: {result.VerificationErrors}", result.AllExpectedDocumentsPresent ? "PHIS Verification Complete" : "PHIS Verification Requires Review", MessageBoxButtons.OK, result.AllExpectedDocumentsPresent ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (PhisDocumentPresencePreconditionException ex)
        {
            string examples = string.Join(Environment.NewLine, ex.Examples.Select(item => $"{item.ClientId} | {item.DocumentTitle}: {item.Reason}"));
            MessageBox.Show(this, $"Verification_Upload.csv is not ready for PHIS verification.\n\nBlocking rows: {ex.InvalidStatusRows}\n{examples}", "PHIS Verification Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(this, ex.Message, "PHIS Verification Input Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "PHIS Verification Report Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(this, ex.Message, "PHIS Verification CSV Invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            LoggerService.LogError($"PHIS document presence verification failed: {ex.Message}", ex);
            MessageBox.Show(this, $"PHIS document verification could not complete.\n\n{ex.Message}", "PHIS Verification Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            foreach ((Control control, bool wasEnabled) in capturedStates) control.Enabled = wasEnabled;
            btn_VerifyDocumentsOnPhis.Enabled = verifierWasEnabled;
            btn_VerifyDocumentsOnPhis.Text = verifierText;
            _phisPresenceVerificationRunning = false;
        }
    }

    private static void EnsureReportCanBeCommitted(string reportPath)
    {
        if (!File.Exists(reportPath)) throw new IOException("Document_Reconciliation_Audit.txt was not found. Run Document Reconciliation Audit first.");
        using (new FileStream(reportPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        string probePath = reportPath + ".presence-probe-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (File.Create(probePath)) { } }
        finally { if (File.Exists(probePath)) File.Delete(probePath); }
    }
}
