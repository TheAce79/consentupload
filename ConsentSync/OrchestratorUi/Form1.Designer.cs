namespace OrchestratorUi
{
    partial class Form1
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
            grpConfig.SuspendLayout();
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
            grpConfig.Size = new Size(459, 242);
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
            rtxt_Log.Location = new Point(12, 330);
            rtxt_Log.Name = "rtxt_Log";
            rtxt_Log.ReadOnly = true;
            rtxt_Log.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtxt_Log.Size = new Size(763, 185);
            rtxt_Log.TabIndex = 1;
            rtxt_Log.Text = "";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(803, 537);
            Controls.Add(grpConfig);
            Controls.Add(rtxt_Log);
            Name = "Form1";
            Text = "ConsentSync";
            grpConfig.ResumeLayout(false);
            grpConfig.PerformLayout();
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
    }
}