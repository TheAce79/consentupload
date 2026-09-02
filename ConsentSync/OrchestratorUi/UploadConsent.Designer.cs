
namespace OrchestratorUi
{
    partial class UploadConsent
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(UploadConsent));
            toolTip1 = new ToolTip(components);
            bt_ValidatePdf = new Button();
            bt_GenerateCsv = new Button();
            bt_Upload = new Button();
            bt_AppendFileRose = new Button();
            btn_ClientIdentityPreAudit = new Button();
            btn_DocumentReconciliationAudit = new Button();
            txt_OriginalDigitalConsentCount = new TextBox();
            txt_ExpectedManualConsentCount = new TextBox();
            txt_ExpectedFileRoseCount = new TextBox();
            lbl_OriginalDigitalConsentCount = new Label();
            lbl_ExpectedManualConsentCount = new Label();
            lbl_ExpectedFileRoseCount = new Label();
            grpConfig = new GroupBox();
            cb_Grade = new ComboBox();
            lb_Grade = new Label();
            txt_SchoolName = new TextBox();
            lb_School = new Label();
            txtBox_BatchSize = new TextBox();
            label1 = new Label();
            bt_Save = new Button();
            bt_ScanPdfOcr = new Button();
            bt_ScanPdf = new Button();
            rtxt_Log = new RichTextBox();
            folderBrowserDialog1 = new FolderBrowserDialog();
            gb_Normalise = new GroupBox();
            btn_ExtractBulk = new Button();
            btn_ProcessCsv = new Button();
            gb_Phase1 = new GroupBox();
            btn_ExportMassImms = new Button();
            bt_SearchClientId = new Button();
            pb_Phase1 = new ProgressBar();
            lbl_Phase1Progress = new Label();
            gb_PreUpload = new GroupBox();
            gb_UploadPhis = new GroupBox();
            pb_Phase3 = new ProgressBar();
            lbl_Phase3Progress = new Label();
            grp_Phase4Auditing = new GroupBox();
            groupBox1 = new GroupBox();
            btn_PortableChrome = new Button();
            lb_Dir = new Label();
            txt_BaseDir = new TextBox();
            btn_BrowseDir = new Button();
            bt_PdfMerge = new Button();
            tx_PdfOutputFileName = new TextBox();
            bt_PdfSplit = new Button();
            tx_PdfSplitPages = new TextBox();
            grpConfig.SuspendLayout();
            gb_Normalise.SuspendLayout();
            gb_Phase1.SuspendLayout();
            gb_PreUpload.SuspendLayout();
            gb_UploadPhis.SuspendLayout();
            grp_Phase4Auditing.SuspendLayout();
            groupBox1.SuspendLayout();
            SuspendLayout();
            // 
            // bt_ValidatePdf
            // 
            bt_ValidatePdf.BackColor = Color.FromArgb(30, 110, 180);
            bt_ValidatePdf.FlatAppearance.BorderColor = Color.FromArgb(20, 80, 140);
            bt_ValidatePdf.FlatStyle = FlatStyle.Flat;
            bt_ValidatePdf.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_ValidatePdf.ForeColor = Color.White;
            bt_ValidatePdf.Location = new Point(12, 60);
            bt_ValidatePdf.Name = "bt_ValidatePdf";
            bt_ValidatePdf.Size = new Size(270, 38);
            bt_ValidatePdf.TabIndex = 0;
            bt_ValidatePdf.Text = "🔍 Validate PDFs Against Student Records";
            toolTip1.SetToolTip(bt_ValidatePdf, resources.GetString("bt_ValidatePdf.ToolTip"));
            bt_ValidatePdf.UseVisualStyleBackColor = false;
            bt_ValidatePdf.Click += bt_ValidatePdf_Click;
            // 
            // bt_GenerateCsv
            // 
            bt_GenerateCsv.BackColor = Color.FromArgb(0, 130, 100);
            bt_GenerateCsv.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 75);
            bt_GenerateCsv.FlatStyle = FlatStyle.Flat;
            bt_GenerateCsv.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_GenerateCsv.ForeColor = Color.White;
            bt_GenerateCsv.Location = new Point(12, 175);
            bt_GenerateCsv.Name = "bt_GenerateCsv";
            bt_GenerateCsv.Size = new Size(270, 38);
            bt_GenerateCsv.TabIndex = 1;
            bt_GenerateCsv.Text = "📄 Generate Upload PDF && CSV ";
            toolTip1.SetToolTip(bt_GenerateCsv, resources.GetString("bt_GenerateCsv.ToolTip"));
            bt_GenerateCsv.UseVisualStyleBackColor = false;
            bt_GenerateCsv.Click += bt_GenerateCsv_Click;
            // 
            // bt_Upload
            // 
            bt_Upload.BackColor = Color.FromArgb(140, 30, 30);
            bt_Upload.FlatAppearance.BorderColor = Color.FromArgb(100, 20, 20);
            bt_Upload.FlatStyle = FlatStyle.Flat;
            bt_Upload.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_Upload.ForeColor = Color.White;
            bt_Upload.Location = new Point(12, 60);
            bt_Upload.Name = "bt_Upload";
            bt_Upload.Size = new Size(275, 41);
            bt_Upload.TabIndex = 0;
            bt_Upload.Text = "⬆️  Upload Consent & FileRose to PHIS";
            toolTip1.SetToolTip(bt_Upload, resources.GetString("bt_Upload.ToolTip"));
            bt_Upload.UseVisualStyleBackColor = false;
            bt_Upload.Click += bt_Upload_Click;
            // 
            // bt_AppendFileRose
            // 
            bt_AppendFileRose.BackColor = Color.FromArgb(130, 60, 160);
            bt_AppendFileRose.FlatAppearance.BorderColor = Color.FromArgb(100, 40, 130);
            bt_AppendFileRose.FlatStyle = FlatStyle.Flat;
            bt_AppendFileRose.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_AppendFileRose.ForeColor = Color.White;
            bt_AppendFileRose.Location = new Point(12, 231);
            bt_AppendFileRose.Name = "bt_AppendFileRose";
            bt_AppendFileRose.Size = new Size(270, 38);
            bt_AppendFileRose.TabIndex = 2;
            bt_AppendFileRose.Text = "🌹 Append FileRose Rows to CSV";
            toolTip1.SetToolTip(bt_AppendFileRose, resources.GetString("bt_AppendFileRose.ToolTip"));
            bt_AppendFileRose.UseVisualStyleBackColor = false;
            bt_AppendFileRose.Click += bt_AppendFileRose_Click;
            // 
            // btn_ClientIdentityPreAudit
            // 
            btn_ClientIdentityPreAudit.BackColor = Color.FromArgb(50, 110, 85);
            btn_ClientIdentityPreAudit.FlatAppearance.BorderColor = Color.FromArgb(35, 80, 60);
            btn_ClientIdentityPreAudit.FlatStyle = FlatStyle.Flat;
            btn_ClientIdentityPreAudit.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btn_ClientIdentityPreAudit.ForeColor = Color.White;
            btn_ClientIdentityPreAudit.Location = new Point(12, 45);
            btn_ClientIdentityPreAudit.Name = "btn_ClientIdentityPreAudit";
            btn_ClientIdentityPreAudit.Size = new Size(292, 43);
            btn_ClientIdentityPreAudit.TabIndex = 0;
            btn_ClientIdentityPreAudit.Text = "🔍 Client Identity Pre-Audit (Optional)";
            toolTip1.SetToolTip(btn_ClientIdentityPreAudit, "Checks uploaded Client IDs and student names against the exported Mass Imms roster before handoff to the independent auditor.\r\n\r\nThis check does not modify upload data.");
            btn_ClientIdentityPreAudit.UseVisualStyleBackColor = false;
            btn_ClientIdentityPreAudit.Click += btn_ClientIdentityPreAudit_Click;
            // 
            // btn_DocumentReconciliationAudit
            // 
            btn_DocumentReconciliationAudit.BackColor = Color.FromArgb(50, 110, 85);
            btn_DocumentReconciliationAudit.FlatAppearance.BorderColor = Color.FromArgb(35, 80, 60);
            btn_DocumentReconciliationAudit.FlatStyle = FlatStyle.Flat;
            btn_DocumentReconciliationAudit.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btn_DocumentReconciliationAudit.ForeColor = Color.White;
            btn_DocumentReconciliationAudit.Location = new Point(12, 164);
            btn_DocumentReconciliationAudit.Name = "btn_DocumentReconciliationAudit";
            btn_DocumentReconciliationAudit.Size = new Size(292, 43);
            btn_DocumentReconciliationAudit.TabIndex = 4;
            btn_DocumentReconciliationAudit.Text = "Document Reconciliation Audit";
            toolTip1.SetToolTip(btn_DocumentReconciliationAudit, "Reconciles successful verification rows against archived PDFs. This audit does not modify CSV or PDF files.");
            btn_DocumentReconciliationAudit.UseVisualStyleBackColor = false;
            btn_DocumentReconciliationAudit.Click += btn_DocumentReconciliationAudit_Click;
            //
            // lbl_OriginalDigitalConsentCount
            //
            lbl_OriginalDigitalConsentCount.AutoSize = true;
            lbl_OriginalDigitalConsentCount.Location = new Point(12, 98);
            lbl_OriginalDigitalConsentCount.Name = "lbl_OriginalDigitalConsentCount";
            lbl_OriginalDigitalConsentCount.Size = new Size(176, 15);
            lbl_OriginalDigitalConsentCount.Text = "Original Digital Consents (SNB)";
            //
            // txt_OriginalDigitalConsentCount
            //
            txt_OriginalDigitalConsentCount.Location = new Point(205, 95);
            txt_OriginalDigitalConsentCount.Name = "txt_OriginalDigitalConsentCount";
            txt_OriginalDigitalConsentCount.Size = new Size(99, 23);
            txt_OriginalDigitalConsentCount.TabIndex = 1;
            toolTip1.SetToolTip(txt_OriginalDigitalConsentCount, "Enter the total number of digital consent submissions shown for this school/grade batch on the SNB website. This value is confirmed manually by the uploader/verifier. Phase 4 does not access the SNB website.");
            //
            // lbl_ExpectedManualConsentCount
            //
            lbl_ExpectedManualConsentCount.AutoSize = true;
            lbl_ExpectedManualConsentCount.Location = new Point(12, 122);
            lbl_ExpectedManualConsentCount.Name = "lbl_ExpectedManualConsentCount";
            lbl_ExpectedManualConsentCount.Size = new Size(132, 15);
            lbl_ExpectedManualConsentCount.Text = "Manual Consent Forms";
            //
            // txt_ExpectedManualConsentCount
            //
            txt_ExpectedManualConsentCount.Location = new Point(205, 119);
            txt_ExpectedManualConsentCount.Name = "txt_ExpectedManualConsentCount";
            txt_ExpectedManualConsentCount.Size = new Size(99, 23);
            txt_ExpectedManualConsentCount.TabIndex = 2;
            toolTip1.SetToolTip(txt_ExpectedManualConsentCount, "Enter the number of physical manual consent forms identified in the original school batch. Count each submitted manual consent form once, including a form later merged with a digital consent PDF.");
            //
            // lbl_ExpectedFileRoseCount
            //
            lbl_ExpectedFileRoseCount.AutoSize = true;
            lbl_ExpectedFileRoseCount.Location = new Point(12, 146);
            lbl_ExpectedFileRoseCount.Name = "lbl_ExpectedFileRoseCount";
            lbl_ExpectedFileRoseCount.Size = new Size(91, 15);
            lbl_ExpectedFileRoseCount.Text = "FileRose Forms";
            //
            // txt_ExpectedFileRoseCount
            //
            txt_ExpectedFileRoseCount.Location = new Point(205, 143);
            txt_ExpectedFileRoseCount.Name = "txt_ExpectedFileRoseCount";
            txt_ExpectedFileRoseCount.Size = new Size(99, 23);
            txt_ExpectedFileRoseCount.TabIndex = 3;
            toolTip1.SetToolTip(txt_ExpectedFileRoseCount, "Enter the number of physical FileRose forms identified in the original school batch. Each scanned FileRose PDF page represents one physical FileRose form; multiple pages in one PDF are separate forms merged before upload.");
            // 
            // grpConfig
            // 
            grpConfig.Controls.Add(cb_Grade);
            grpConfig.Controls.Add(lb_Grade);
            grpConfig.Controls.Add(txt_SchoolName);
            grpConfig.Controls.Add(lb_School);
            grpConfig.Controls.Add(txtBox_BatchSize);
            grpConfig.Controls.Add(label1);
            grpConfig.Location = new Point(50, 154);
            grpConfig.Name = "grpConfig";
            grpConfig.Size = new Size(412, 176);
            grpConfig.TabIndex = 0;
            grpConfig.TabStop = false;
            grpConfig.Text = "School Context";
            // 
            // cb_Grade
            // 
            cb_Grade.DropDownStyle = ComboBoxStyle.DropDownList;
            cb_Grade.FormattingEnabled = true;
            cb_Grade.Location = new Point(125, 122);
            cb_Grade.Name = "cb_Grade";
            cb_Grade.Size = new Size(151, 28);
            cb_Grade.TabIndex = 6;
            // 
            // lb_Grade
            // 
            lb_Grade.AutoSize = true;
            lb_Grade.Location = new Point(6, 118);
            lb_Grade.Name = "lb_Grade";
            lb_Grade.Size = new Size(49, 20);
            lb_Grade.TabIndex = 5;
            lb_Grade.Text = "Grade";
            // 
            // txt_SchoolName
            // 
            txt_SchoolName.Location = new Point(125, 78);
            txt_SchoolName.Name = "txt_SchoolName";
            txt_SchoolName.Size = new Size(151, 27);
            txt_SchoolName.TabIndex = 4;
            // 
            // lb_School
            // 
            lb_School.AutoSize = true;
            lb_School.Location = new Point(6, 78);
            lb_School.Name = "lb_School";
            lb_School.Size = new Size(98, 20);
            lb_School.TabIndex = 3;
            lb_School.Text = "School Name";
            // 
            // txtBox_BatchSize
            // 
            txtBox_BatchSize.Location = new Point(125, 39);
            txtBox_BatchSize.Name = "txtBox_BatchSize";
            txtBox_BatchSize.Size = new Size(151, 27);
            txtBox_BatchSize.TabIndex = 2;
            txtBox_BatchSize.TextChanged += txtBox_BatchSize_TextChanged;
            txtBox_BatchSize.KeyPress += txtBox_BatchSize_KeyPress;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(6, 46);
            label1.Name = "label1";
            label1.Size = new Size(77, 20);
            label1.TabIndex = 1;
            label1.Text = "Batch Size";
            // 
            // bt_Save
            // 
            bt_Save.BackColor = Color.SeaGreen;
            bt_Save.FlatStyle = FlatStyle.Flat;
            bt_Save.ForeColor = Color.White;
            bt_Save.Location = new Point(56, 360);
            bt_Save.Name = "bt_Save";
            bt_Save.Size = new Size(126, 32);
            bt_Save.TabIndex = 15;
            bt_Save.Text = "💾 Save Configuration";
            bt_Save.UseVisualStyleBackColor = false;
            bt_Save.Click += btn_SaveConfig_Click;
            // 
            // bt_ScanPdfOcr
            // 
            bt_ScanPdfOcr.BackColor = Color.SeaGreen;
            bt_ScanPdfOcr.FlatStyle = FlatStyle.Flat;
            bt_ScanPdfOcr.ForeColor = Color.White;
            bt_ScanPdfOcr.Location = new Point(203, 357);
            bt_ScanPdfOcr.Name = "bt_ScanPdfOcr";
            bt_ScanPdfOcr.Size = new Size(202, 35);
            bt_ScanPdfOcr.TabIndex = 13;
            bt_ScanPdfOcr.Text = "\U0001f9ea  Scanned PDF OCR";
            bt_ScanPdfOcr.UseVisualStyleBackColor = false;
            bt_ScanPdfOcr.Click += bt_ScanPdfOcr_Click;
            // 
            // bt_ScanPdf
            // 
            bt_ScanPdf.BackColor = Color.SeaGreen;
            bt_ScanPdf.FlatStyle = FlatStyle.Flat;
            bt_ScanPdf.ForeColor = Color.White;
            bt_ScanPdf.Location = new Point(12, 117);
            bt_ScanPdf.Name = "bt_ScanPdf";
            bt_ScanPdf.Size = new Size(270, 35);
            bt_ScanPdf.TabIndex = 12;
            bt_ScanPdf.Text = "\U0001f9ea  Scanned PDF → CSV ";
            bt_ScanPdf.UseVisualStyleBackColor = false;
            bt_ScanPdf.Click += bt_ScanPdf_Click;
            // 
            // rtxt_Log
            // 
            rtxt_Log.BackColor = Color.Black;
            rtxt_Log.Font = new Font("Consolas", 9F);
            rtxt_Log.ForeColor = Color.LimeGreen;
            rtxt_Log.Location = new Point(12, 544);
            rtxt_Log.Name = "rtxt_Log";
            rtxt_Log.ReadOnly = true;
            rtxt_Log.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtxt_Log.Size = new Size(1094, 147);
            rtxt_Log.TabIndex = 1;
            rtxt_Log.Text = "";
            // 
            // gb_Normalise
            // 
            gb_Normalise.Controls.Add(btn_ExtractBulk);
            gb_Normalise.Controls.Add(btn_ProcessCsv);
            gb_Normalise.Location = new Point(468, 12);
            gb_Normalise.Name = "gb_Normalise";
            gb_Normalise.Size = new Size(300, 120);
            gb_Normalise.TabIndex = 2;
            gb_Normalise.TabStop = false;
            gb_Normalise.Text = "Phase 0 — Pre-Processing";
            // 
            // btn_ExtractBulk
            // 
            btn_ExtractBulk.BackColor = Color.DarkOrange;
            btn_ExtractBulk.FlatStyle = FlatStyle.Flat;
            btn_ExtractBulk.ForeColor = Color.White;
            btn_ExtractBulk.Location = new Point(12, 28);
            btn_ExtractBulk.Name = "btn_ExtractBulk";
            btn_ExtractBulk.Size = new Size(254, 34);
            btn_ExtractBulk.TabIndex = 0;
            btn_ExtractBulk.Text = "📄 Extract Bulk PDF";
            btn_ExtractBulk.UseVisualStyleBackColor = false;
            btn_ExtractBulk.Click += btn_ExtractBulk_Click;
            // 
            // btn_ProcessCsv
            // 
            btn_ProcessCsv.BackColor = Color.Teal;
            btn_ProcessCsv.FlatStyle = FlatStyle.Flat;
            btn_ProcessCsv.ForeColor = Color.White;
            btn_ProcessCsv.Location = new Point(12, 70);
            btn_ProcessCsv.Name = "btn_ProcessCsv";
            btn_ProcessCsv.Size = new Size(254, 34);
            btn_ProcessCsv.TabIndex = 1;
            btn_ProcessCsv.Text = "📋 Process CSV";
            btn_ProcessCsv.UseVisualStyleBackColor = false;
            btn_ProcessCsv.Click += btn_ProcessCsv_Click;
            // 
            // gb_Phase1
            // 
            gb_Phase1.Controls.Add(btn_ExportMassImms);
            gb_Phase1.Controls.Add(bt_SearchClientId);
            gb_Phase1.Controls.Add(pb_Phase1);
            gb_Phase1.Controls.Add(lbl_Phase1Progress);
            gb_Phase1.Location = new Point(790, 12);
            gb_Phase1.Name = "gb_Phase1";
            gb_Phase1.Size = new Size(316, 120);
            gb_Phase1.TabIndex = 3;
            gb_Phase1.TabStop = false;
            gb_Phase1.Text = "Phase 1 — PHIS Client ID Search";
            // 
            // btn_ExportMassImms
            // 
            btn_ExportMassImms.BackColor = Color.DarkSlateBlue;
            btn_ExportMassImms.FlatAppearance.BorderColor = Color.FromArgb(48, 60, 120);
            btn_ExportMassImms.FlatStyle = FlatStyle.Flat;
            btn_ExportMassImms.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
            btn_ExportMassImms.ForeColor = Color.White;
            btn_ExportMassImms.Location = new Point(12, 28);
            btn_ExportMassImms.Name = "btn_ExportMassImms";
            btn_ExportMassImms.Size = new Size(146, 38);
            btn_ExportMassImms.TabIndex = 0;
            btn_ExportMassImms.Text = "📋 Export Mass Imms Roster to CSV";
            btn_ExportMassImms.UseVisualStyleBackColor = false;
            btn_ExportMassImms.Click += btn_ExportMassImms_Click;
            // 
            // bt_SearchClientId
            // 
            bt_SearchClientId.BackColor = Color.FromArgb(0, 90, 160);
            bt_SearchClientId.FlatAppearance.BorderColor = Color.FromArgb(0, 60, 120);
            bt_SearchClientId.FlatStyle = FlatStyle.Flat;
            bt_SearchClientId.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_SearchClientId.ForeColor = Color.White;
            bt_SearchClientId.Location = new Point(164, 28);
            bt_SearchClientId.Name = "bt_SearchClientId";
            bt_SearchClientId.Size = new Size(140, 38);
            bt_SearchClientId.TabIndex = 1;
            bt_SearchClientId.Text = "🔍 Search Client IDs on PHIS";
            bt_SearchClientId.UseVisualStyleBackColor = false;
            bt_SearchClientId.Click += bt_SearchClientId_Click;
            // 
            // pb_Phase1
            // 
            pb_Phase1.Location = new Point(12, 76);
            pb_Phase1.Name = "pb_Phase1";
            pb_Phase1.Size = new Size(292, 14);
            pb_Phase1.Style = ProgressBarStyle.Continuous;
            pb_Phase1.TabIndex = 1;
            // 
            // lbl_Phase1Progress
            // 
            lbl_Phase1Progress.Font = new Font("Segoe UI", 8F);
            lbl_Phase1Progress.ForeColor = Color.FromArgb(0, 90, 160);
            lbl_Phase1Progress.Location = new Point(12, 94);
            lbl_Phase1Progress.Name = "lbl_Phase1Progress";
            lbl_Phase1Progress.Size = new Size(292, 18);
            lbl_Phase1Progress.TabIndex = 2;
            lbl_Phase1Progress.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // gb_PreUpload
            // 
            gb_PreUpload.Controls.Add(bt_AppendFileRose);
            gb_PreUpload.Controls.Add(bt_ScanPdf);
            gb_PreUpload.Controls.Add(bt_ValidatePdf);
            gb_PreUpload.Controls.Add(bt_GenerateCsv);
            gb_PreUpload.Location = new Point(468, 140);
            gb_PreUpload.Name = "gb_PreUpload";
            gb_PreUpload.Size = new Size(300, 290);
            gb_PreUpload.TabIndex = 3;
            gb_PreUpload.TabStop = false;
            gb_PreUpload.Text = "Phase 2 — PDF Validation & Upload Preparation";
            // 
            // gb_UploadPhis
            // 
            gb_UploadPhis.Controls.Add(bt_Upload);
            gb_UploadPhis.Controls.Add(pb_Phase3);
            gb_UploadPhis.Controls.Add(lbl_Phase3Progress);
            gb_UploadPhis.Location = new Point(790, 140);
            gb_UploadPhis.Name = "gb_UploadPhis";
            gb_UploadPhis.Size = new Size(316, 160);
            gb_UploadPhis.TabIndex = 4;
            gb_UploadPhis.TabStop = false;
            gb_UploadPhis.Text = "Phase 3 — PHIS Document Upload";
            // 
            // pb_Phase3
            // 
            pb_Phase3.Location = new Point(12, 118);
            pb_Phase3.Name = "pb_Phase3";
            pb_Phase3.Size = new Size(274, 14);
            pb_Phase3.Style = ProgressBarStyle.Continuous;
            pb_Phase3.TabIndex = 1;
            // 
            // lbl_Phase3Progress
            // 
            lbl_Phase3Progress.Font = new Font("Segoe UI", 8F);
            lbl_Phase3Progress.ForeColor = Color.FromArgb(140, 30, 30);
            lbl_Phase3Progress.Location = new Point(12, 136);
            lbl_Phase3Progress.Name = "lbl_Phase3Progress";
            lbl_Phase3Progress.Size = new Size(228, 24);
            lbl_Phase3Progress.TabIndex = 2;
            lbl_Phase3Progress.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // grp_Phase4Auditing
            // 
            grp_Phase4Auditing.Controls.Add(btn_DocumentReconciliationAudit);
            grp_Phase4Auditing.Controls.Add(txt_ExpectedFileRoseCount);
            grp_Phase4Auditing.Controls.Add(lbl_ExpectedFileRoseCount);
            grp_Phase4Auditing.Controls.Add(txt_ExpectedManualConsentCount);
            grp_Phase4Auditing.Controls.Add(lbl_ExpectedManualConsentCount);
            grp_Phase4Auditing.Controls.Add(txt_OriginalDigitalConsentCount);
            grp_Phase4Auditing.Controls.Add(lbl_OriginalDigitalConsentCount);
            grp_Phase4Auditing.Controls.Add(btn_ClientIdentityPreAudit);
            grp_Phase4Auditing.Location = new Point(790, 310);
            grp_Phase4Auditing.Name = "grp_Phase4Auditing";
            grp_Phase4Auditing.Size = new Size(316, 225);
            grp_Phase4Auditing.TabIndex = 5;
            grp_Phase4Auditing.TabStop = false;
            grp_Phase4Auditing.Text = "Phase 4 — Post-Upload Audit & Review";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(btn_PortableChrome);
            groupBox1.Controls.Add(lb_Dir);
            groupBox1.Controls.Add(txt_BaseDir);
            groupBox1.Controls.Add(btn_BrowseDir);
            groupBox1.Location = new Point(50, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(412, 136);
            groupBox1.TabIndex = 3;
            groupBox1.TabStop = false;
            groupBox1.Text = "Configuraration";
            // 
            // btn_PortableChrome
            // 
            btn_PortableChrome.BackColor = Color.SteelBlue;
            btn_PortableChrome.FlatStyle = FlatStyle.Flat;
            btn_PortableChrome.ForeColor = Color.White;
            btn_PortableChrome.Location = new Point(87, 80);
            btn_PortableChrome.Name = "btn_PortableChrome";
            btn_PortableChrome.Size = new Size(126, 32);
            btn_PortableChrome.TabIndex = 13;
            btn_PortableChrome.Text = "🌐 Download Portable Chrome";
            btn_PortableChrome.UseVisualStyleBackColor = false;
            btn_PortableChrome.Click += btn_PortableChrome_Click;
            // 
            // lb_Dir
            // 
            lb_Dir.AutoSize = true;
            lb_Dir.Location = new Point(6, 38);
            lb_Dir.Name = "lb_Dir";
            lb_Dir.Size = new Size(64, 20);
            lb_Dir.TabIndex = 10;
            lb_Dir.Text = "Base Dir";
            // 
            // txt_BaseDir
            // 
            txt_BaseDir.Location = new Point(76, 38);
            txt_BaseDir.Name = "txt_BaseDir";
            txt_BaseDir.ReadOnly = true;
            txt_BaseDir.Size = new Size(200, 27);
            txt_BaseDir.TabIndex = 11;
            // 
            // btn_BrowseDir
            // 
            btn_BrowseDir.Location = new Point(286, 38);
            btn_BrowseDir.Name = "btn_BrowseDir";
            btn_BrowseDir.Size = new Size(90, 27);
            btn_BrowseDir.TabIndex = 12;
            btn_BrowseDir.Text = "📁 Browse";
            btn_BrowseDir.UseVisualStyleBackColor = true;
            btn_BrowseDir.Click += btn_BrowseDir_Click;
            // 
            // bt_PdfMerge
            // 
            bt_PdfMerge.BackColor = Color.FromArgb(70, 100, 140);
            bt_PdfMerge.FlatAppearance.BorderColor = Color.FromArgb(50, 75, 110);
            bt_PdfMerge.FlatStyle = FlatStyle.Flat;
            bt_PdfMerge.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_PdfMerge.ForeColor = Color.White;
            bt_PdfMerge.Location = new Point(203, 403);
            bt_PdfMerge.Name = "bt_PdfMerge";
            bt_PdfMerge.Size = new Size(141, 32);
            bt_PdfMerge.TabIndex = 14;
            bt_PdfMerge.Text = "🔀 Merge PDFs";
            bt_PdfMerge.UseVisualStyleBackColor = false;
            bt_PdfMerge.Click += bt_PdfMerge_Click;
            // 
            // tx_PdfOutputFileName
            // 
            tx_PdfOutputFileName.Location = new Point(56, 403);
            tx_PdfOutputFileName.Name = "tx_PdfOutputFileName";
            tx_PdfOutputFileName.PlaceholderText = "merged.pdf";
            tx_PdfOutputFileName.Size = new Size(140, 27);
            tx_PdfOutputFileName.TabIndex = 14;
            // 
            // bt_PdfSplit
            // 
            bt_PdfSplit.BackColor = Color.FromArgb(70, 100, 140);
            bt_PdfSplit.FlatAppearance.BorderColor = Color.FromArgb(50, 75, 110);
            bt_PdfSplit.FlatStyle = FlatStyle.Flat;
            bt_PdfSplit.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_PdfSplit.ForeColor = Color.White;
            bt_PdfSplit.Location = new Point(203, 441);
            bt_PdfSplit.Name = "bt_PdfSplit";
            bt_PdfSplit.Size = new Size(141, 32);
            bt_PdfSplit.TabIndex = 15;
            bt_PdfSplit.Text = "✂ Split PDF";
            bt_PdfSplit.UseVisualStyleBackColor = false;
            bt_PdfSplit.Click += bt_PdfSplit_Click;
            // 
            // tx_PdfSplitPages
            // 
            tx_PdfSplitPages.Location = new Point(56, 441);
            tx_PdfSplitPages.Name = "tx_PdfSplitPages";
            tx_PdfSplitPages.PlaceholderText = "pages/file";
            tx_PdfSplitPages.Size = new Size(140, 27);
            tx_PdfSplitPages.TabIndex = 15;
            // 
            // UploadConsent
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1137, 706);
            Controls.Add(bt_PdfSplit);
            Controls.Add(tx_PdfSplitPages);
            Controls.Add(bt_PdfMerge);
            Controls.Add(tx_PdfOutputFileName);
            Controls.Add(bt_ScanPdfOcr);
            Controls.Add(bt_Save);
            Controls.Add(groupBox1);
            Controls.Add(grp_Phase4Auditing);
            Controls.Add(gb_UploadPhis);
            Controls.Add(gb_PreUpload);
            Controls.Add(gb_Phase1);
            Controls.Add(gb_Normalise);
            Controls.Add(grpConfig);
            Controls.Add(rtxt_Log);
            Name = "UploadConsent";
            Text = "ConsentSync — Immunization Consent & Document Upload Manager";
            grpConfig.ResumeLayout(false);
            grpConfig.PerformLayout();
            gb_Normalise.ResumeLayout(false);
            gb_Phase1.ResumeLayout(false);
            gb_PreUpload.ResumeLayout(false);
            gb_UploadPhis.ResumeLayout(false);
            grp_Phase4Auditing.ResumeLayout(false);
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox grpConfig;
        private Label label1;
        private TextBox txtBox_BatchSize;
        private TextBox txt_SchoolName;
        private Label lb_School;
        private Label lb_Grade;
        private ComboBox cb_Grade;
        private RichTextBox rtxt_Log;
        private FolderBrowserDialog folderBrowserDialog1;
        private GroupBox gb_Normalise;
        private Button btn_ExtractBulk;
        private Button btn_ProcessCsv;
        private GroupBox gb_Phase1;
        private Button btn_ExportMassImms;
        private Button bt_SearchClientId;
        private ProgressBar pb_Phase1;
        private Label lbl_Phase1Progress;
        private GroupBox gb_PreUpload;
        private Button bt_ValidatePdf;
        private Button bt_GenerateCsv;
        private ToolTip toolTip1;
        private GroupBox gb_UploadPhis;
        private GroupBox grp_Phase4Auditing;
        private Button btn_ClientIdentityPreAudit;
        private Button btn_DocumentReconciliationAudit;
        private TextBox txt_OriginalDigitalConsentCount;
        private TextBox txt_ExpectedManualConsentCount;
        private TextBox txt_ExpectedFileRoseCount;
        private Label lbl_OriginalDigitalConsentCount;
        private Label lbl_ExpectedManualConsentCount;
        private Label lbl_ExpectedFileRoseCount;
        private Button bt_Upload;
        private Button bt_AppendFileRose;
        private ProgressBar pb_Phase3;
        private Label lbl_Phase3Progress;
        private Button bt_ScanPdf;
        private Button bt_ScanPdfOcr;
        private GroupBox groupBox1;
        private Button bt_Save;
        private Button btn_PortableChrome;
        private Label lb_Dir;
        private TextBox txt_BaseDir;
        private Button btn_BrowseDir;
        private Button bt_PdfMerge;
        private TextBox tx_PdfOutputFileName;
        private Button bt_PdfSplit;
        private TextBox tx_PdfSplitPages;
    }
}
