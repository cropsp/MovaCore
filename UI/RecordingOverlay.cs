using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace MovaCore.UI
{
    /// <summary>
    /// A small pill near the bottom of the screen that shows "Recording" with the microphone level, then
    /// "Transcribing…", or a short message. It never takes the focus (the text must go to the window the user is
    /// typing in) and lets clicks through.
    /// </summary>
    internal sealed partial class RecordingOverlay : Form
    {
        // Sizes in pixels at 96 DPI
        private const int PillHeight = 40;
        private const int PillWidth = 200;
        private const int MaxMessageWidth = 420;
        private const int BottomGap = 56;
        private const int BarCount = 16;

        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        private static readonly Color Background = Color.FromArgb(32, 32, 32);
        private static readonly Color RecordingColor = Color.FromArgb(239, 68, 68);
        private static readonly Color TranscribingColor = Color.FromArgb(245, 158, 11);

        private readonly Font _font = new("Segoe UI", 10F, FontStyle.Regular);
        private readonly Func<float> _level;
        private readonly float[] _levels = new float[BarCount]; // a short history, newest last
        private readonly Timer _animation = new() { Interval = 33 };
        private readonly Timer _hideTimer = new();
        private Mode _mode = Mode.Hidden;
        private Rectangle _area;
        private string _text = "";
        private int _frame;

        /// <param name="level">The microphone level from 0 to 1, read about 30 times a second while recording.</param>
        public RecordingOverlay(Func<float> level)
        {
            _level = level;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Background;
            ForeColor = Color.White;
            Font = _font;
            Size = new Size(PillWidth, PillHeight);
            Opacity = 0.92; // also makes the window layered, which WS_EX_TRANSPARENT needs to let clicks through
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            _animation.Tick += OnAnimationTick;
            _hideTimer.Tick += (_, _) => HideOverlay();
        }

        private enum Mode
        {
            Hidden,
            Recording,
            Transcribing,
            Message,
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public void ShowRecording()
        {
            Array.Clear(_levels);
            Present(Mode.Recording, Strings.OverlayRecording, hideAfter: null);
        }

        public void ShowTranscribing() => Present(Mode.Transcribing, Strings.OverlayTranscribing, hideAfter: null);

        public void ShowMessage(string message) => Present(Mode.Message, message, hideAfter: TimeSpan.FromSeconds(3.5));

        public void HideOverlay()
        {
            _hideTimer.Stop();
            _animation.Stop();
            _mode = Mode.Hidden;
            if (Visible) Hide();
        }

        private void Present(Mode mode, string text, TimeSpan? hideAfter)
        {
            _mode = mode;
            _text = text;
            _hideTimer.Stop();
            if (hideAfter is { } delay)
            {
                _hideTimer.Interval = (int)delay.TotalMilliseconds;
                _hideTimer.Start();
            }

            // The screen the user is working on: the one under the mouse pointer
            _area = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (!Visible)
            {
                // Placed on the right monitor before it shows, so that WinForms scales it for that monitor's DPI
                Location = new Point(_area.Left + _area.Width / 2, _area.Bottom - LogicalToDeviceUnits(BottomGap));
                Show(); // without activation, see ShowWithoutActivation
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                RoundCorners();
            }
            Relayout();

            if (mode == Mode.Message) _animation.Stop();
            else _animation.Start();
            Invalidate();
        }

        // Bottom center of the working area; a message gets as wide as its text needs
        private void Relayout()
        {
            int width = LogicalToDeviceUnits(PillWidth);
            if (_mode == Mode.Message)
            {
                int textWidth = TextRenderer.MeasureText(_text, Font).Width + LogicalToDeviceUnits(48);
                width = Math.Clamp(textWidth, width, LogicalToDeviceUnits(MaxMessageWidth));
            }
            int height = LogicalToDeviceUnits(PillHeight);
            Bounds = new Rectangle(
                _area.Left + (_area.Width - width) / 2,
                _area.Bottom - LogicalToDeviceUnits(BottomGap) - height,
                width,
                height);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (_mode != Mode.Hidden) Relayout();
        }

        private void RoundCorners()
        {
            // Windows 11 rounds the corners of a borderless window on request; Windows 10 keeps them square
            int preference = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            _frame++;
            if (_mode == Mode.Recording)
            {
                Array.Copy(_levels, 1, _levels, 0, BarCount - 1);
                _levels[^1] = Math.Clamp(_level(), 0f, 1f);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Background);

            int pad = LogicalToDeviceUnits(14);
            int dot = LogicalToDeviceUnits(10);
            int centerY = ClientSize.Height / 2;
            var textFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

            if (_mode == Mode.Message)
            {
                var bounds = new Rectangle(pad, 0, ClientSize.Width - 2 * pad, ClientSize.Height);
                TextRenderer.DrawText(g, _text, Font, bounds, ForeColor, textFlags | TextFormatFlags.HorizontalCenter);
                return;
            }

            // A pulsing dot: red while recording, amber while transcribing
            Color color = _mode == Mode.Recording ? RecordingColor : TranscribingColor;
            int alpha = 160 + (int)(95 * (0.5 + 0.5 * Math.Sin(_frame / 5.0)));
            using (var brush = new SolidBrush(Color.FromArgb(alpha, color)))
                g.FillEllipse(brush, pad, centerY - dot / 2, dot, dot);

            int textLeft = pad + dot + LogicalToDeviceUnits(8);
            Size textSize = TextRenderer.MeasureText(_text, Font);
            TextRenderer.DrawText(g, _text, Font, new Rectangle(textLeft, 0, textSize.Width, ClientSize.Height), ForeColor, textFlags);

            if (_mode != Mode.Recording) return;

            // The level of the last half second as bars, newest on the right
            int barsLeft = textLeft + textSize.Width + LogicalToDeviceUnits(8);
            int barsWidth = ClientSize.Width - pad - barsLeft;
            if (barsWidth <= 0) return;
            float step = barsWidth / (float)BarCount;
            float barWidth = Math.Max(1f, step * 0.55f);
            float maxHeight = ClientSize.Height * 0.55f;
            using var barBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            for (int i = 0; i < BarCount; i++)
            {
                // Speech peaks around -30..-6 dBFS: a square root makes quiet speech visible
                float height = Math.Max(LogicalToDeviceUnits(2), maxHeight * MathF.Sqrt(_levels[i]));
                g.FillRectangle(barBrush, barsLeft + i * step, centerY - height / 2, barWidth, height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                _hideTimer.Dispose();
                _font.Dispose();
            }
            base.Dispose(disposing);
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [LibraryImport("dwmapi.dll")]
        private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
