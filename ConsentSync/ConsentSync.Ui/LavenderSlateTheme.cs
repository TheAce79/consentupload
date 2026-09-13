using System.Drawing.Drawing2D;

namespace ConsentSync.Ui;

public static class LavenderSlatePalette
{
    public static readonly Color Window = Color.FromArgb(0xF4, 0xF0, 0xF9);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(0xE2, 0xDD, 0xF0);
    public static readonly Color Header = Color.FromArgb(0xEB, 0xE4, 0xF6);
    public static readonly Color Slate = Color.FromArgb(0x3C, 0x2E, 0x59);
    public static readonly Color MutedText = Color.FromArgb(0x5A, 0x52, 0x66);
    public static readonly Color AlternateRow = Color.FromArgb(0xFA, 0xF8, 0xFC);
    public static readonly Color Selection = Color.FromArgb(0x7C, 0x5C, 0xFC);
    public static readonly Color Primary = Color.FromArgb(0x6D, 0x4A, 0xFF);
    public static readonly Color PrimaryHover = Color.FromArgb(0x5B, 0x3C, 0xE1);
    public static readonly Color SecondaryHover = Color.FromArgb(0xDF, 0xD5, 0xF2);
    public static readonly Color Disabled = Color.FromArgb(0xE9, 0xEC, 0xEF);
    public static readonly Color DisabledText = Color.FromArgb(0xAD, 0xAD, 0xB8);
    public static readonly Color Gridline = Color.FromArgb(0xF0, 0xEC, 0xF7);
    public static readonly Color Warning = Color.FromArgb(0x8A, 0x5A, 0x00);
    public static readonly Color Error = Color.FromArgb(0x9B, 0x24, 0x2D);
}

public enum LavenderButtonKind { Secondary, Primary }

public sealed class LavenderCardPanel : Panel
{
    public LavenderCardPanel()
    {
        BackColor = LavenderSlatePalette.Card;
        Padding = new Padding(12);
        Margin = new Padding(0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 4);
        using var pen = new Pen(LavenderSlatePalette.Border);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public sealed class LavenderTabControl : TabControl
{
    public LavenderTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Normal;
        Padding = new Point(16, 8);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;
        var bounds = GetTabRect(e.Index);
        using var background = new SolidBrush(selected ? LavenderSlatePalette.Card : LavenderSlatePalette.Window);
        e.Graphics.FillRectangle(background, bounds);
        if (selected)
        {
            using var selectedFont = new Font(Font, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, selectedFont, bounds, LavenderSlatePalette.Slate,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else
        {
            TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, bounds, LavenderSlatePalette.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        if (selected)
        {
            using var indicator = new SolidBrush(LavenderSlatePalette.Selection);
            e.Graphics.FillRectangle(indicator, bounds.X + 6, bounds.Bottom - 3, Math.Max(0, bounds.Width - 12), 3);
        }
        if ((e.State & DrawItemState.Focus) != 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4));
    }
}

public static class LavenderSlateTheme
{
    public static void Apply(Form form)
    {
        form.BackColor = LavenderSlatePalette.Window;
        form.ForeColor = LavenderSlatePalette.Slate;
        ApplyControls(form.Controls);
    }

    public static void ApplyControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            control.EnabledChanged -= ContainerEnabledChanged;
            control.EnabledChanged += ContainerEnabledChanged;
            switch (control)
            {
                case Button button:
                    ApplyButton(button);
                    break;
                case DataGridView grid:
                    ApplyGrid(grid);
                    break;
                case RichTextBox log:
                    log.BackColor = LavenderSlatePalette.Card;
                    log.ForeColor = LavenderSlatePalette.Slate;
                    log.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case TextBoxBase input:
                    input.BackColor = LavenderSlatePalette.Card;
                    input.ForeColor = LavenderSlatePalette.Slate;
                    break;
                case ComboBox combo:
                    combo.BackColor = LavenderSlatePalette.Card;
                    combo.ForeColor = LavenderSlatePalette.Slate;
                    break;
                case DateTimePicker picker:
                    picker.CalendarMonthBackground = LavenderSlatePalette.Card;
                    picker.CalendarForeColor = LavenderSlatePalette.Slate;
                    break;
                case GroupBox group:
                    ApplyGroupBox(group);
                    break;
                case TabPage page:
                    page.BackColor = LavenderSlatePalette.Window;
                    page.ForeColor = LavenderSlatePalette.Slate;
                    break;
                case Panel panel when panel is not LavenderCardPanel:
                    panel.BackColor = LavenderSlatePalette.Window;
                    break;
            }
            ApplyControls(control.Controls);
        }
    }

    public static void ApplyButton(Button button, LavenderButtonKind kind = LavenderButtonKind.Secondary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = LavenderSlatePalette.Border;
        button.FlatAppearance.BorderSize = 1;
        button.UseVisualStyleBackColor = false;
        button.Padding = new Padding(4, 2, 4, 2);
        button.BackColor = kind == LavenderButtonKind.Primary ? LavenderSlatePalette.Primary : LavenderSlatePalette.Header;
        button.ForeColor = kind == LavenderButtonKind.Primary ? Color.White : LavenderSlatePalette.Slate;
        button.Font = new Font(button.Font, kind == LavenderButtonKind.Primary ? FontStyle.Bold : button.Font.Style);
        button.MouseEnter -= ButtonMouseEnter;
        button.MouseLeave -= ButtonMouseLeave;
        button.EnabledChanged -= ButtonEnabledChanged;
        button.MouseEnter += ButtonMouseEnter;
        button.MouseLeave += ButtonMouseLeave;
        button.EnabledChanged += ButtonEnabledChanged;
        button.Tag = kind;
        UpdateButtonState(button);
    }

    public static void ApplyGrid(DataGridView grid)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = LavenderSlatePalette.Card;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = LavenderSlatePalette.Gridline;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = LavenderSlatePalette.Header, ForeColor = LavenderSlatePalette.Slate,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold), SelectionBackColor = LavenderSlatePalette.Header,
            SelectionForeColor = LavenderSlatePalette.Slate, Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        grid.RowsDefaultCellStyle.BackColor = LavenderSlatePalette.Card;
        grid.RowsDefaultCellStyle.ForeColor = LavenderSlatePalette.Slate;
        grid.AlternatingRowsDefaultCellStyle.BackColor = LavenderSlatePalette.AlternateRow;
        grid.DefaultCellStyle.SelectionBackColor = LavenderSlatePalette.Selection;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = LavenderSlatePalette.Selection;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;
        grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 30);
    }

    private static void ApplyGroupBox(GroupBox group)
    {
        group.BackColor = LavenderSlatePalette.Card;
        group.ForeColor = LavenderSlatePalette.Slate;
        group.Padding = new Padding(12, 26, 12, 12);
        group.Paint -= GroupPaint;
        group.Paint += GroupPaint;
    }

    private static void GroupPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not GroupBox group) return;
        var bounds = new Rectangle(0, group.Font.Height / 2, group.Width - 1, group.Height - group.Font.Height / 2 - 1);
        using var pen = new Pen(LavenderSlatePalette.Border);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    private static void ButtonMouseEnter(object? sender, EventArgs e)
    {
        if (sender is Button button && button.Enabled)
            button.BackColor = button.Tag is LavenderButtonKind.Primary ? LavenderSlatePalette.PrimaryHover : LavenderSlatePalette.SecondaryHover;
    }

    private static void ButtonMouseLeave(object? sender, EventArgs e)
    {
        if (sender is Button button) UpdateButtonState(button);
    }

    private static void ButtonEnabledChanged(object? sender, EventArgs e)
    {
        if (sender is Button button) UpdateButtonState(button);
    }

    private static void UpdateButtonState(Button button)
    {
        if (!IsEffectivelyEnabled(button))
        {
            button.BackColor = LavenderSlatePalette.Disabled;
            button.ForeColor = LavenderSlatePalette.DisabledText;
            return;
        }
        bool primary = button.Tag is LavenderButtonKind.Primary;
        button.BackColor = primary ? LavenderSlatePalette.Primary : LavenderSlatePalette.Header;
        button.ForeColor = primary ? Color.White : LavenderSlatePalette.Slate;
    }

    private static bool IsEffectivelyEnabled(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
            if (!current.Enabled) return false;
        return true;
    }

    private static void ContainerEnabledChanged(object? sender, EventArgs e)
    {
        if (sender is Control control) UpdateNestedButtons(control.Controls);
    }

    private static void UpdateNestedButtons(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            if (control is Button button) UpdateButtonState(button);
            UpdateNestedButtons(control.Controls);
        }
    }
}
