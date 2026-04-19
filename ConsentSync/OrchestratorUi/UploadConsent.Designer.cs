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
            toolTip1 = new ToolTip();      
            grpConfig = new GroupBox();
            lb_Dir = new Label();
            txt_BaseDir = new TextBox();
            btn_BrowseDir = new Button();
            cb_Grade = new ComboBox();
            lb_Grade = new Label();
            txt_SchoolName = new TextBox();
            lb_School = new Label();
            txtBox_BatchSize = new TextBox();
            label1 = new Label();
            btn_SaveConfig = new Button();
            btn_PortableChrome = new Button();
            rtxt_Log = new RichTextBox();
            folderBrowserDialog1 = new FolderBrowserDialog();
            gb_Normalise = new GroupBox();
            btn_ExtractBulk = new Button();
            btn_ProcessCsv = new Button();
            gb_Phase1 = new GroupBox();
            bt_SearchClientId = new Button();
            gb_PreUpload = new GroupBox();
            bt_GenerateCsv = new Button();
            bt_ValidatePdf = new Button();
            grpConfig.SuspendLayout();
            gb_Normalise.SuspendLayout();
            gb_Phase1.SuspendLayout();
            gb_PreUpload.SuspendLayout();
            SuspendLayout();
            // 
            // grpConfig
            // 
            grpConfig.Controls.Add(lb_Dir);
            grpConfig.Controls.Add(txt_BaseDir);
            grpConfig.Controls.Add(btn_BrowseDir);
            grpConfig.Controls.Add(cb_Grade);
            grpConfig.Controls.Add(lb_Grade);
            grpConfig.Controls.Add(txt_SchoolName);
            grpConfig.Controls.Add(lb_School);
            grpConfig.Controls.Add(txtBox_BatchSize);
            grpConfig.Controls.Add(label1);
            grpConfig.Controls.Add(btn_SaveConfig);
            grpConfig.Controls.Add(btn_PortableChrome);
            grpConfig.Location = new Point(12, 12);
            grpConfig.Name = "grpConfig";
            grpConfig.Size = new Size(440, 268);
            grpConfig.TabIndex = 0;
            grpConfig.TabStop = false;
            grpConfig.Text = "Configuration";
            // 
            // lb_Dir
            // 
            lb_Dir.AutoSize = true;
            lb_Dir.Location = new Point(6, 33);
            lb_Dir.Name = "lb_Dir";
            lb_Dir.Size = new Size(64, 20);
            lb_Dir.TabIndex = 7;
            lb_Dir.Text = "Base Dir";
            // 
            // txt_BaseDir
            // 
            txt_BaseDir.Location = new Point(116, 30);
            txt_BaseDir.Name = "txt_BaseDir";
            txt_BaseDir.ReadOnly = true;
            txt_BaseDir.Size = new Size(200, 27);
            txt_BaseDir.TabIndex = 8;
            // 
            // btn_BrowseDir
            // 
            btn_BrowseDir.Location = new Point(322, 29);
            btn_BrowseDir.Name = "btn_BrowseDir";
            btn_BrowseDir.Size = new Size(90, 27);
            btn_BrowseDir.TabIndex = 9;
            btn_BrowseDir.Text = "📁 Browse";
            btn_BrowseDir.UseVisualStyleBackColor = true;
            btn_BrowseDir.Click += btn_BrowseDir_Click;
            // 
            // cb_Grade
            // 
            cb_Grade.DropDownStyle = ComboBoxStyle.DropDownList;
            cb_Grade.FormattingEnabled = true;
            cb_Grade.Location = new Point(116, 175);
            cb_Grade.Name = "cb_Grade";
            cb_Grade.Size = new Size(151, 28);
            cb_Grade.TabIndex = 6;
            // 
            // lb_Grade
            // 
            lb_Grade.AutoSize = true;
            lb_Grade.Location = new Point(6, 178);
            lb_Grade.Name = "lb_Grade";
            lb_Grade.Size = new Size(49, 20);
            lb_Grade.TabIndex = 5;
            lb_Grade.Text = "Grade";
            // 
            // txt_SchoolName
            // 
            txt_SchoolName.Location = new Point(116, 130);
            txt_SchoolName.Name = "txt_SchoolName";
            txt_SchoolName.Size = new Size(151, 27);
            txt_SchoolName.TabIndex = 4;
            // 
            // lb_School
            // 
            lb_School.AutoSize = true;
            lb_School.Location = new Point(6, 130);
            lb_School.Name = "lb_School";
            lb_School.Size = new Size(98, 20);
            lb_School.TabIndex = 3;
            lb_School.Text = "School Name";
            // 
            // txtBox_BatchSize
            // 
            txtBox_BatchSize.Location = new Point(116, 86);
            txtBox_BatchSize.Name = "txtBox_BatchSize";
            txtBox_BatchSize.Size = new Size(151, 27);
            txtBox_BatchSize.TabIndex = 2;
            txtBox_BatchSize.TextChanged += txtBox_BatchSize_TextChanged;
            txtBox_BatchSize.KeyPress += txtBox_BatchSize_KeyPress;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(6, 86);
            label1.Name = "label1";
            label1.Size = new Size(77, 20);
            label1.TabIndex = 1;
            label1.Text = "Batch Size";
            // 
            // btn_SaveConfig
            // 
            btn_SaveConfig.BackColor = Color.SeaGreen;
            btn_SaveConfig.FlatStyle = FlatStyle.Flat;
            btn_SaveConfig.ForeColor = Color.White;
            btn_SaveConfig.Location = new Point(286, 86);
            btn_SaveConfig.Name = "btn_SaveConfig";
            btn_SaveConfig.Size = new Size(126, 32);
            btn_SaveConfig.TabIndex = 10;
            btn_SaveConfig.Text = "💾 Save Configuration";
            btn_SaveConfig.UseVisualStyleBackColor = false;
            btn_SaveConfig.Click += btn_SaveConfig_Click;
            // 
            // btn_PortableChrome
            // 
            btn_PortableChrome.BackColor = Color.SteelBlue;
            btn_PortableChrome.FlatStyle = FlatStyle.Flat;
            btn_PortableChrome.ForeColor = Color.White;
            btn_PortableChrome.Location = new Point(286, 130);
            btn_PortableChrome.Name = "btn_PortableChrome";
            btn_PortableChrome.Size = new Size(126, 32);
            btn_PortableChrome.TabIndex = 11;
            btn_PortableChrome.Text = "🌐 Download Portable Chrome";
            btn_PortableChrome.UseVisualStyleBackColor = false;
            btn_PortableChrome.Click += btn_PortableChrome_Click;
            // 
            // rtxt_Log
            // 
            rtxt_Log.BackColor = Color.Black;
            rtxt_Log.Font = new Font("Consolas", 9F);
            rtxt_Log.ForeColor = Color.LimeGreen;
            rtxt_Log.Location = new Point(42, 301);
            rtxt_Log.Name = "rtxt_Log";
            rtxt_Log.ReadOnly = true;
            rtxt_Log.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtxt_Log.Size = new Size(977, 276);
            rtxt_Log.TabIndex = 1;
            rtxt_Log.Text = "";
            // 
            // gb_Normalise
            // 
            gb_Normalise.Controls.Add(btn_ExtractBulk);
            gb_Normalise.Controls.Add(btn_ProcessCsv);
            gb_Normalise.Location = new Point(468, 26);
            gb_Normalise.Name = "gb_Normalise";
            gb_Normalise.Size = new Size(280, 120);
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
            gb_Phase1.Controls.Add(bt_SearchClientId);
            gb_Phase1.Location = new Point(765, 26);
            gb_Phase1.Name = "gb_Phase1";
            gb_Phase1.Size = new Size(254, 120);
            gb_Phase1.TabIndex = 3;
            gb_Phase1.TabStop = false;
            gb_Phase1.Text = "Phase 1 — PHIS Client ID Search";
            // 
            // bt_SearchClientId
            // 
            bt_SearchClientId.BackColor = Color.FromArgb(0, 90, 160);
            bt_SearchClientId.FlatAppearance.BorderColor = Color.FromArgb(0, 60, 120);
            bt_SearchClientId.FlatStyle = FlatStyle.Flat;
            bt_SearchClientId.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_SearchClientId.ForeColor = Color.White;
            bt_SearchClientId.Location = new Point(12, 30);
            bt_SearchClientId.Name = "bt_SearchClientId";
            bt_SearchClientId.Size = new Size(228, 38);
            bt_SearchClientId.TabIndex = 0;
            bt_SearchClientId.Text = "🔍 Search Client IDs on PHIS";
            bt_SearchClientId.UseVisualStyleBackColor = false;
            bt_SearchClientId.Click += bt_SearchClientId_Click;

            // 
            // gb_PreUpload
            // 
            gb_PreUpload.Controls.Add(bt_ValidatePdf);
            gb_PreUpload.Controls.Add(bt_GenerateCsv);
            gb_PreUpload.Location = new Point(468, 152);
            gb_PreUpload.Name = "gb_PreUpload";
            gb_PreUpload.Size = new Size(280, 147);
            gb_PreUpload.TabIndex = 3;
            gb_PreUpload.TabStop = false;
            gb_PreUpload.Text = "Phase 2 — PDF Validation & Upload Preparation";
            // 
            // bt_ValidatePdf
            // 
            bt_ValidatePdf.BackColor = Color.FromArgb(30, 110, 180);
            bt_ValidatePdf.FlatAppearance.BorderColor = Color.FromArgb(20, 80, 140);
            bt_ValidatePdf.FlatStyle = FlatStyle.Flat;
            bt_ValidatePdf.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_ValidatePdf.ForeColor = Color.White;
            bt_ValidatePdf.Location = new Point(12, 28);
            bt_ValidatePdf.Name = "bt_ValidatePdf";
            bt_ValidatePdf.Size = new Size(254, 38);
            bt_ValidatePdf.TabIndex = 0;
            bt_ValidatePdf.Text = "🔍 Validate PDFs Against Student Records";
            bt_ValidatePdf.UseVisualStyleBackColor = false;
            bt_ValidatePdf.Click += bt_ValidatePdf_Click;
            toolTip1.SetToolTip(bt_ValidatePdf,
                "Scans all PDFs in the Output Ready folder and matches\r\n" +
                "each one to a student record using name + fuzzy matching.\r\n\r\n" +
                "✅  Matched PDFs  → ready for upload\r\n" +
                "⚠️  Unmatched PDFs → copied to 6_Error for manual review\r\n\r\n" +
                "Produces Validation_Results.csv — review it before\r\n" +
                "generating the Upload CSV.");
            // 
            // bt_GenerateCsv
            // 
            bt_GenerateCsv.BackColor = Color.FromArgb(0, 130, 100);
            bt_GenerateCsv.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 75);
            bt_GenerateCsv.FlatStyle = FlatStyle.Flat;
            bt_GenerateCsv.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bt_GenerateCsv.ForeColor = Color.White;
            bt_GenerateCsv.Location = new Point(12, 76);
            bt_GenerateCsv.Name = "bt_GenerateCsv";
            bt_GenerateCsv.Size = new Size(254, 38);
            bt_GenerateCsv.TabIndex = 1;
            bt_GenerateCsv.Text = "📄 Generate Upload CSV";
            bt_GenerateCsv.UseVisualStyleBackColor = false;
            bt_GenerateCsv.Click += bt_GenerateCsv_Click;
            toolTip1.SetToolTip(bt_GenerateCsv,
                "Reads Validation_Results.csv (produced by Validate PDFs)\r\n" +
                "and generates Upload_to_PHIS.csv for Phase 3.\r\n\r\n" +
                "✅  Validated records  → added to Upload CSV\r\n" +
                "⚠️  Missing PDFs       → skipped and logged\r\n" +
                "📋  FileRose records   → appended automatically\r\n\r\n" +
                "Run 'Validate PDFs' first, review errors,\r\n" +
                "then click this button to prepare the upload.");


            // 
            // UploadConsent
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1065, 589);
            Controls.Add(gb_PreUpload);
            Controls.Add(gb_Phase1);
            Controls.Add(gb_Normalise);
            Controls.Add(grpConfig);
            Controls.Add(rtxt_Log);
            Name = "UploadConsent";
            Text = "ConsentSync";
            grpConfig.ResumeLayout(false);
            grpConfig.PerformLayout();
            gb_Normalise.ResumeLayout(false);
            gb_Phase1.ResumeLayout(false);
            gb_PreUpload.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private GroupBox grpConfig;
        private Label label1;
        private TextBox txtBox_BatchSize;
        private TextBox txt_SchoolName;
        private Label lb_School;
        private Label lb_Grade;
        private ComboBox cb_Grade;
        private Label lb_Dir;
        private TextBox txt_BaseDir;
        private Button btn_BrowseDir;
        private Button btn_SaveConfig;
        private Button btn_PortableChrome;
        private RichTextBox rtxt_Log;
        private FolderBrowserDialog folderBrowserDialog1;
        private GroupBox gb_Normalise;

        private Button btn_ExtractBulk;
        private Button btn_ProcessCsv;
        private GroupBox gb_Phase1;
        private Button bt_SearchClientId;
        private GroupBox gb_PreUpload;
        private Button bt_ValidatePdf;
        private Button bt_GenerateCsv;
        private ToolTip toolTip1;
    }
}