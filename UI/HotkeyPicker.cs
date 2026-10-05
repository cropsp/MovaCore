using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MovaCore.Models;
using MovaCore.Services;

namespace MovaCore.UI
{
    /// <summary>A hotkey and a Change button that records a new one through the keyboard hook.</summary>
    internal sealed class HotkeyPicker : TableLayoutPanel
    {
        private readonly Label _label = new();
        private readonly Button _button = new();
        private readonly Func<CancellationToken, Task<Hotkey?>> _captureHotkey;
        private readonly CancellationToken _cancellation;
        private readonly Func<Hotkey, string?> _validate;

        /// <param name="validate">Returns why a recorded hotkey cannot be used, or null if it can.</param>
        public HotkeyPicker(
            Hotkey value,
            int width,
            Func<CancellationToken, Task<Hotkey?>> captureHotkey,
            Func<Hotkey, string?> validate,
            CancellationToken cancellation)
        {
            Value = value;
            _captureHotkey = captureHotkey;
            _validate = validate;
            _cancellation = cancellation;

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            ColumnCount = 2;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RowCount = 1;

            _label.Text = value.ToString();
            _label.AutoSize = true;
            // The recording prompt is longer than a hotkey: wrap it instead of pushing the button out
            _label.MaximumSize = new Size(width, 0);
            _label.Anchor = AnchorStyles.Left;

            _button.Text = Strings.HotkeyChange;
            _button.Anchor = AnchorStyles.Right;
            _button.Click += OnChangeClick;

            Controls.Add(_label, 0, 0);
            Controls.Add(_button, 1, 0);
        }

        public Hotkey Value { get; private set; }

        public Button Button => _button;

        /// <summary>Raised when recording starts or ends, so the form can disable Save meanwhile.</summary>
        public event EventHandler<bool>? CaptureStateChanged;

        private async void OnChangeClick(object? sender, EventArgs e)
        {
            _button.Enabled = false;
            _label.Text = Strings.HotkeyPrompt;
            CaptureStateChanged?.Invoke(this, true);

            Hotkey? captured = null;
            try
            {
                captured = await _captureHotkey(_cancellation);
            }
            catch (OperationCanceledException)
            {
                // The form was closed while recording
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not record the hotkey", ex);
            }

            // The form may be gone by now: closing it cancels the recording
            if (IsDisposed) return;

            _label.Text = Value.ToString();
            _button.Enabled = true;
            CaptureStateChanged?.Invoke(this, false);
            _button.Focus();

            if (captured is not { } hotkey) return;
            if (_validate(hotkey) is { } problem)
            {
                MessageBox.Show(FindForm(), problem, "MovaCore", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Value = hotkey;
            _label.Text = hotkey.ToString();
        }
    }
}
