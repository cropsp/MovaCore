using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MovaCore.Services;
using Timer = System.Windows.Forms.Timer;

namespace MovaCore.UI
{
    /// <summary>
    /// A small pill near the bottom of the screen: while recording, a red dot and an equalizer of the voice (grey and
    /// pulsing until the microphone delivers sound); while transcribing, if that takes a moment, an amber wave; or a
    /// short message. It never takes the focus (the text must go to the window the user is typing in) and lets clicks
    /// through. The look follows Handy's overlay.
    /// </summary>
    internal sealed partial class RecordingOverlay : Form
    {
        // Sizes in pixels at 96 DPI
        private const int PillHeight = 36;
        private const int PillWidth = 108;
        private const int MaxMessageWidth = 420;
        private const int BottomGap = 56;
        private const int EdgePadding = 14;
        private const int DotSize = 8;
        private const int VisibleBars = 9; // the lower bands, where the voice is
        private const float BarWidth = 4, BarGap = 3, MinBarHeight = 3, MaxBarHeight = 18, PulseBarHeight = 6;

        private const double MaxOpacity = 0.92; // below 1 also keeps the window layered, which click-through needs
        private const double FadeInMs = 120, FadeOutMs = 200;
        private const double PulseMs = 900, PulseStaggerMs = 75;

        /// <summary>A quick transcription shows no animation at all, only a slower one does.</summary>
        private const double ProcessingDelayMs = 300;

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
        private static readonly Color WaitingColor = Color.FromArgb(160, 160, 160);
        private static readonly Color BarColor = Color.FromArgb(230, 255, 255, 255);

        private readonly Font _font = new("Segoe UI", 10F, FontStyle.Regular);
        private readonly Func<float[], int> _recentAudio;
        private readonly float[] _samples = new float[SpectrumAnalyzer.WindowSize];
        private readonly SpectrumAnalyzer _spectrum = new();
        private readonly Timer _animation = new() { Interval = 33 };
        private readonly Timer _hideTimer = new();
        private Mode _mode = Mode.Hidden;
        private Rectangle _area;
        private string _text = "";
        private bool _live; // recording, and the microphone has delivered sound
        private long _modeStarted;
        private long _fadeStarted;
        private double _fadeFrom;
        private bool _fadingOut;

        /// <param name="recentAudio">
        /// Fills the buffer with the latest audio and returns how much of it is real (0 until the microphone delivers);
        /// called about 30 times a second while recording.
        /// </param>
        public RecordingOverlay(Func<float[], int> recentAudio)
        {
            _recentAudio = recentAudio;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Background;
            ForeColor = Color.White;
            Font = _font;
            Size = new Size(PillWidth, PillHeight);
            Opacity = MaxOpacity;
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
            _live = false;
            _spectrum.Reset();
            Present(Mode.Recording, "", hideAfter: null);
        }

        // The bars keep falling while it starts; the wave appears only if it takes a moment
        public void ShowTranscribing() => Present(Mode.Transcribing, "", hideAfter: null);

        public void ShowMessage(string message) => Present(Mode.Message, message, hideAfter: TimeSpan.FromSeconds(3.5));

        /// <summary>Fades out (still showing what it showed) and hides.</summary>
        public void HideOverlay()
        {
            _hideTimer.Stop();
            if (!Visible)
            {
                _mode = Mode.Hidden;
                return;
            }
            if (!_fadingOut) StartFade(fadeOut: true);
        }

        private void Present(Mode mode, string text, TimeSpan? hideAfter)
        {
            _mode = mode;
            _text = text;
            _modeStarted = Stopwatch.GetTimestamp();
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
                Opacity = 0.01; // not 0: a fully transparent layered window may not get painted before the fade
                Show(); // without activation, see ShowWithoutActivation
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                RoundCorners();
                StartFade(fadeOut: false);
            }
            else if (_fadingOut)
            {
                StartFade(fadeOut: false); // shown again while it was fading out
            }
            Relayout();

            _animation.Start();
            Invalidate();
        }

        private void StartFade(bool fadeOut)
        {
            _fadingOut = fadeOut;
            _fadeFrom = Opacity;
            _fadeStarted = Stopwatch.GetTimestamp();
            _animation.Start();
        }

        // Called on every animation tick
        private void UpdateFade()
        {
            double elapsed = Stopwatch.GetElapsedTime(_fadeStarted).TotalMilliseconds;
            if (_fadingOut)
            {
                double progress = Math.Min(1, elapsed / FadeOutMs);
                if (progress >= 1)
                {
                    _fadingOut = false;
                    _mode = Mode.Hidden;
                    _animation.Stop();
                    Hide();
                    return;
                }
                Opacity = Math.Max(0.01, _fadeFrom * (1 - progress));
            }
            else if (Opacity < MaxOpacity)
            {
                double progress = Math.Min(1, elapsed / FadeInMs);
                Opacity = _fadeFrom + (MaxOpacity - _fadeFrom) * progress;
            }
        }

        // Bottom center of the working area; a message gets as wide as its text needs
        private void Relayout()
        {
            int width = LogicalToDeviceUnits(PillWidth);
            if (_mode == Mode.Message)
            {
                int textWidth = TextRenderer.MeasureText(_text, Font).Width + LogicalToDeviceUnits(48);
                width = Math.Clamp(textWidth, LogicalToDeviceUnits(200), LogicalToDeviceUnits(MaxMessageWidth));
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
            UpdateFade();
            if (!Visible) return;
            if (_mode == Mode.Message && !_fadingOut && Opacity >= MaxOpacity)
            {
                _animation.Stop(); // a message does not move
                return;
            }

            if (_mode == Mode.Recording)
            {
                int count = _recentAudio(_samples);
                if (count > 0) _live = true;
                if (_live) _spectrum.Update(_samples);
            }
            else
            {
                _spectrum.Update(ReadOnlySpan<float>.Empty); // the bars fall
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Background);

            int pad = LogicalToDeviceUnits(EdgePadding);
            if (_mode == Mode.Message)
            {
                var textFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.HorizontalCenter;
                var bounds = new Rectangle(pad, 0, ClientSize.Width - 2 * pad, ClientSize.Height);
                TextRenderer.DrawText(g, _text, Font, bounds, ForeColor, textFlags);
                return;
            }
            if (_mode == Mode.Hidden) return;

            double now = Stopwatch.GetElapsedTime(_modeStarted).TotalMilliseconds;
            bool processing = _mode == Mode.Transcribing && now >= ProcessingDelayMs;
            bool waiting = _mode == Mode.Recording && !_live;

            // The dot: grey until the microphone delivers, then red and breathing; amber while transcribing
            float scale = DeviceDpi / 96f;
            float dot = DotSize * scale;
            float centerY = ClientSize.Height / 2f;
            Color dotColor;
            if (waiting)
            {
                dotColor = Color.FromArgb(115, WaitingColor);
            }
            else
            {
                int alpha = 170 + (int)(85 * (0.5 + 0.5 * Math.Sin(now / 160.0)));
                dotColor = Color.FromArgb(alpha, _mode == Mode.Recording ? RecordingColor : TranscribingColor);
            }
            using (var dotBrush = new SolidBrush(dotColor))
                g.FillEllipse(dotBrush, pad, centerY - dot / 2, dot, dot);

            // The bars, centred in the space right of the dot, growing up and down from the middle
            float barWidth = BarWidth * scale, gap = BarGap * scale;
            float barsWidth = VisibleBars * barWidth + (VisibleBars - 1) * gap;
            float areaLeft = pad + dot;
            float left = areaLeft + (ClientSize.Width - pad - areaLeft - barsWidth) / 2;
            ReadOnlySpan<float> levels = _spectrum.Levels;
            for (int i = 0; i < VisibleBars; i++)
            {
                float height;
                Color color;
                if (waiting || processing)
                {
                    // A wave from the outer bars to the middle: still waiting (grey), or working (amber)
                    double delay = (VisibleBars / 2 - Math.Abs(i - VisibleBars / 2)) * PulseStaggerMs;
                    double phase = ((now - delay) % PulseMs + PulseMs) % PulseMs / PulseMs;
                    float swing = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
                    height = PulseBarHeight * (0.55f + 0.95f * swing);
                    color = Color.FromArgb((int)(255 * (0.25f + 0.45f * swing)), waiting ? WaitingColor : TranscribingColor);
                }
                else
                {
                    height = Math.Clamp(MinBarHeight + MathF.Pow(levels[i], 0.7f) * (MaxBarHeight - MinBarHeight), MinBarHeight, MaxBarHeight);
                    color = BarColor;
                }
                using var brush = new SolidBrush(color);
                FillBar(g, brush, left + i * (barWidth + gap), centerY, barWidth, height * scale);
            }
        }

        // A bar with fully rounded ends
        private static void FillBar(Graphics g, Brush brush, float x, float centerY, float width, float height)
        {
            if (height <= width)
            {
                g.FillEllipse(brush, x, centerY - height / 2, width, height);
                return;
            }
            float top = centerY - height / 2;
            using var path = new GraphicsPath();
            path.AddArc(x, top, width, width, 180, 180);
            path.AddArc(x, top + height - width, width, width, 0, 180);
            path.CloseFigure();
            g.FillPath(brush, path);
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
