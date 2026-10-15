using System.Drawing;
using System.Windows.Forms;

namespace MovaCore.UI
{
    /// <summary>
    /// The building blocks of MovaCore's windows: auto-sizing panels, so that longer (translated) texts and larger fonts
    /// are never clipped, and the window follows. Sizes are in pixels at 96 DPI: AutoScaleMode.Dpi scales them to the
    /// monitor's DPI.
    /// </summary>
    internal static class FormLayout
    {
        /// <summary>Inner width of a group; longer texts wrap instead of widening the window.</summary>
        public const int ContentWidth = 430;

        public const int ButtonMinWidth = 96;
        public const int ButtonMinHeight = 30;

        public static TableLayoutPanel CreatePanel(params ColumnStyle[] columns)
        {
            var panel = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = columns.Length,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
            };
            foreach (ColumnStyle column in columns) panel.ColumnStyles.Add(column);
            return panel;
        }

        public static void AddRow(TableLayoutPanel panel, Control control)
        {
            int row = panel.RowStyles.Count;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowCount = row + 1;
            panel.Controls.Add(control, 0, row);
        }

        public static GroupBox CreateGroup(string title, params Control[] rows)
        {
            TableLayoutPanel content = CreatePanel(new ColumnStyle(SizeType.AutoSize));
            foreach (Control row in rows) AddRow(content, row);

            var group = new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 4, 8, 8),
                Margin = new Padding(0, 0, 0, 8),
            };
            group.Controls.Add(content);
            return group;
        }

        /// <summary>A text that wraps at the content width.</summary>
        public static Label CreateText(string text) => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
        };

        /// <summary>A grey text that wraps at the content width.</summary>
        public static Label CreateHint(string text)
        {
            Label hint = CreateText(text);
            hint.ForeColor = SystemColors.GrayText;
            return hint;
        }

        public static void ConfigureCheckBox(CheckBox checkBox, string text, bool isChecked)
        {
            checkBox.Text = text;
            checkBox.Checked = isChecked;
            checkBox.AutoSize = true;
            checkBox.MaximumSize = new Size(ContentWidth, 0); // wraps a long text instead of widening the form
            checkBox.Anchor = AnchorStyles.Left;
        }

        public static void ConfigureButton(Button button)
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(ButtonMinWidth, ButtonMinHeight);
        }
    }
}
