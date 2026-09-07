namespace CohortUi;

partial class CohortContextForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        grp_CohortContext = new GroupBox();
        btn_LoadContext = new Button();
        cb_SearchClientListName = new ComboBox();
        lbl_SearchClientListName = new Label();
        btn_SaveCohortContext = new Button();
        txt_ClientListName = new TextBox();
        lbl_ClientListName = new Label();
        txt_EncounterGroup = new TextBox();
        lbl_EncounterGroup = new Label();
        txt_Jurisdiction = new TextBox();
        lbl_Jurisdiction = new Label();
        dtp_CohortDate = new DateTimePicker();
        lbl_CohortDate = new Label();
        txt_Type = new TextBox();
        lbl_Type = new Label();
        cb_Location = new ComboBox();
        lbl_Location = new Label();
        cb_Prefix = new ComboBox();
        lbl_Prefix = new Label();
        grp_CohortContext.SuspendLayout();
        SuspendLayout();
        // 
        // grp_CohortContext
        // 
        grp_CohortContext.Controls.Add(btn_LoadContext);
        grp_CohortContext.Controls.Add(cb_SearchClientListName);
        grp_CohortContext.Controls.Add(lbl_SearchClientListName);
        grp_CohortContext.Controls.Add(btn_SaveCohortContext);
        grp_CohortContext.Controls.Add(txt_ClientListName);
        grp_CohortContext.Controls.Add(lbl_ClientListName);
        grp_CohortContext.Controls.Add(txt_EncounterGroup);
        grp_CohortContext.Controls.Add(lbl_EncounterGroup);
        grp_CohortContext.Controls.Add(txt_Jurisdiction);
        grp_CohortContext.Controls.Add(lbl_Jurisdiction);
        grp_CohortContext.Controls.Add(dtp_CohortDate);
        grp_CohortContext.Controls.Add(lbl_CohortDate);
        grp_CohortContext.Controls.Add(txt_Type);
        grp_CohortContext.Controls.Add(lbl_Type);
        grp_CohortContext.Controls.Add(cb_Location);
        grp_CohortContext.Controls.Add(lbl_Location);
        grp_CohortContext.Controls.Add(cb_Prefix);
        grp_CohortContext.Controls.Add(lbl_Prefix);
        grp_CohortContext.Location = new Point(18, 18);
        grp_CohortContext.Name = "grp_CohortContext";
        grp_CohortContext.Size = new Size(596, 414);
        grp_CohortContext.TabIndex = 0;
        grp_CohortContext.TabStop = false;
        grp_CohortContext.Text = "Phase 0 Cohort Context";
        // 
        // btn_LoadContext
        // 
        btn_LoadContext.Location = new Point(455, 38);
        btn_LoadContext.Name = "btn_LoadContext";
        btn_LoadContext.Size = new Size(105, 30);
        btn_LoadContext.TabIndex = 2;
        btn_LoadContext.Text = "Load";
        btn_LoadContext.UseVisualStyleBackColor = true;
        btn_LoadContext.Click += btn_LoadContext_Click;
        // 
        // cb_SearchClientListName
        // 
        cb_SearchClientListName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        cb_SearchClientListName.AutoCompleteSource = AutoCompleteSource.CustomSource;
        cb_SearchClientListName.DropDownStyle = ComboBoxStyle.DropDown;
        cb_SearchClientListName.FormattingEnabled = true;
        cb_SearchClientListName.Location = new Point(160, 39);
        cb_SearchClientListName.Name = "cb_SearchClientListName";
        cb_SearchClientListName.Size = new Size(280, 28);
        cb_SearchClientListName.TabIndex = 1;
        cb_SearchClientListName.SelectionChangeCommitted += cb_SearchClientListName_SelectionChangeCommitted;
        cb_SearchClientListName.KeyDown += cb_SearchClientListName_KeyDown;
        // 
        // lbl_SearchClientListName
        // 
        lbl_SearchClientListName.AutoSize = true;
        lbl_SearchClientListName.Location = new Point(18, 43);
        lbl_SearchClientListName.Name = "lbl_SearchClientListName";
        lbl_SearchClientListName.Size = new Size(126, 20);
        lbl_SearchClientListName.TabIndex = 0;
        lbl_SearchClientListName.Text = "Find saved list";
        // 
        // btn_SaveCohortContext
        // 
        btn_SaveCohortContext.BackColor = Color.SeaGreen;
        btn_SaveCohortContext.FlatStyle = FlatStyle.Flat;
        btn_SaveCohortContext.ForeColor = Color.White;
        btn_SaveCohortContext.Location = new Point(160, 360);
        btn_SaveCohortContext.Name = "btn_SaveCohortContext";
        btn_SaveCohortContext.Size = new Size(180, 34);
        btn_SaveCohortContext.TabIndex = 17;
        btn_SaveCohortContext.Text = "Save Cohort Context";
        btn_SaveCohortContext.UseVisualStyleBackColor = false;
        btn_SaveCohortContext.Click += btn_SaveCohortContext_Click;
        // 
        // txt_ClientListName
        // 
        txt_ClientListName.CharacterCasing = CharacterCasing.Upper;
        txt_ClientListName.Location = new Point(160, 268);
        txt_ClientListName.Name = "txt_ClientListName";
        txt_ClientListName.Size = new Size(400, 27);
        txt_ClientListName.TabIndex = 14;
        txt_ClientListName.TextChanged += txt_ClientListName_TextChanged;
        // 
        // lbl_ClientListName
        // 
        lbl_ClientListName.AutoSize = true;
        lbl_ClientListName.Location = new Point(18, 271);
        lbl_ClientListName.Name = "lbl_ClientListName";
        lbl_ClientListName.Size = new Size(110, 20);
        lbl_ClientListName.TabIndex = 13;
        lbl_ClientListName.Text = "Client list name";
        // 
        // txt_EncounterGroup
        // 
        txt_EncounterGroup.Location = new Point(160, 232);
        txt_EncounterGroup.Name = "txt_EncounterGroup";
        txt_EncounterGroup.Size = new Size(400, 27);
        txt_EncounterGroup.TabIndex = 12;
        // 
        // lbl_EncounterGroup
        // 
        lbl_EncounterGroup.AutoSize = true;
        lbl_EncounterGroup.Location = new Point(18, 235);
        lbl_EncounterGroup.Name = "lbl_EncounterGroup";
        lbl_EncounterGroup.Size = new Size(121, 20);
        lbl_EncounterGroup.TabIndex = 11;
        lbl_EncounterGroup.Text = "Encounter group";
        // 
        // txt_Jurisdiction
        // 
        txt_Jurisdiction.Location = new Point(160, 196);
        txt_Jurisdiction.Name = "txt_Jurisdiction";
        txt_Jurisdiction.Size = new Size(400, 27);
        txt_Jurisdiction.TabIndex = 10;
        // 
        // lbl_Jurisdiction
        // 
        lbl_Jurisdiction.AutoSize = true;
        lbl_Jurisdiction.Location = new Point(18, 199);
        lbl_Jurisdiction.Name = "lbl_Jurisdiction";
        lbl_Jurisdiction.Size = new Size(82, 20);
        lbl_Jurisdiction.TabIndex = 9;
        lbl_Jurisdiction.Text = "Jurisdiction";
        // 
        // dtp_CohortDate
        // 
        dtp_CohortDate.CustomFormat = "yyyy-MM-dd";
        dtp_CohortDate.Format = DateTimePickerFormat.Custom;
        dtp_CohortDate.Location = new Point(160, 159);
        dtp_CohortDate.Name = "dtp_CohortDate";
        dtp_CohortDate.Size = new Size(200, 27);
        dtp_CohortDate.TabIndex = 8;
        dtp_CohortDate.ValueChanged += OnContextParameterChanged;
        // 
        // lbl_CohortDate
        // 
        lbl_CohortDate.AutoSize = true;
        lbl_CohortDate.Location = new Point(18, 163);
        lbl_CohortDate.Name = "lbl_CohortDate";
        lbl_CohortDate.Size = new Size(88, 20);
        lbl_CohortDate.TabIndex = 7;
        lbl_CohortDate.Text = "Cohort date";
        // 
        // txt_Type
        // 
        txt_Type.Location = new Point(438, 122);
        txt_Type.Name = "txt_Type";
        txt_Type.Size = new Size(122, 27);
        txt_Type.TabIndex = 6;
        txt_Type.TextChanged += OnContextParameterChanged;
        // 
        // lbl_Type
        // 
        lbl_Type.AutoSize = true;
        lbl_Type.Location = new Point(386, 125);
        lbl_Type.Name = "lbl_Type";
        lbl_Type.Size = new Size(40, 20);
        lbl_Type.TabIndex = 5;
        lbl_Type.Text = "Type";
        // 
        // cb_Location
        // 
        cb_Location.DropDownStyle = ComboBoxStyle.DropDownList;
        cb_Location.FormattingEnabled = true;
        cb_Location.Location = new Point(160, 122);
        cb_Location.Name = "cb_Location";
        cb_Location.Size = new Size(200, 28);
        cb_Location.TabIndex = 4;
        cb_Location.SelectedIndexChanged += OnContextParameterChanged;
        // 
        // lbl_Location
        // 
        lbl_Location.AutoSize = true;
        lbl_Location.Location = new Point(18, 125);
        lbl_Location.Name = "lbl_Location";
        lbl_Location.Size = new Size(66, 20);
        lbl_Location.TabIndex = 3;
        lbl_Location.Text = "Location";
        // 
        // cb_Prefix
        // 
        cb_Prefix.DropDownStyle = ComboBoxStyle.DropDownList;
        cb_Prefix.FormattingEnabled = true;
        cb_Prefix.Location = new Point(160, 82);
        cb_Prefix.Name = "cb_Prefix";
        cb_Prefix.Size = new Size(200, 28);
        cb_Prefix.TabIndex = 2;
        cb_Prefix.SelectedIndexChanged += OnContextParameterChanged;
        // 
        // lbl_Prefix
        // 
        lbl_Prefix.AutoSize = true;
        lbl_Prefix.Location = new Point(18, 85);
        lbl_Prefix.Name = "lbl_Prefix";
        lbl_Prefix.Size = new Size(45, 20);
        lbl_Prefix.TabIndex = 1;
        lbl_Prefix.Text = "Prefix";
        // 
        // CohortContextForm
        // 
        AutoScaleDimensions = new SizeF(8F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(636, 450);
        Controls.Add(grp_CohortContext);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Name = "CohortContextForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "ConsentSync Cohort Context";
        Load += CohortContextForm_Load;
        grp_CohortContext.ResumeLayout(false);
        grp_CohortContext.PerformLayout();
        ResumeLayout(false);
    }

    private GroupBox grp_CohortContext;
    private ComboBox cb_SearchClientListName;
    private Label lbl_SearchClientListName;
    private Button btn_LoadContext;
    private ComboBox cb_Prefix;
    private Label lbl_Prefix;
    private ComboBox cb_Location;
    private Label lbl_Location;
    private TextBox txt_Type;
    private Label lbl_Type;
    private DateTimePicker dtp_CohortDate;
    private Label lbl_CohortDate;
    private TextBox txt_Jurisdiction;
    private Label lbl_Jurisdiction;
    private TextBox txt_EncounterGroup;
    private Label lbl_EncounterGroup;
    private TextBox txt_ClientListName;
    private Label lbl_ClientListName;
    private Button btn_SaveCohortContext;
}
