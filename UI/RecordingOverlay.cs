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
    /// A small pill near the bottom of the screen with the field mouse from the logo (<see cref="MouseScene"/>): while
    /// recording it sits in grass that grows with the voice (grey and dozing until the microphone delivers sound);
    /// while transcribing, if that takes a moment, it gnaws an ear of wheat; it winks when the text is pasted; and it
    /// goes with short messages. It never takes the focus (the text must go to the window the user is typing in) and
    /// lets clicks through.
    /// </summary>
    internal sealed partial class RecordingOverlay : Form
    {
        // Sizes in pixels at 96 DPI
        private const int PillHeight = (int)MouseScene.Height;
        private const int PillWidth = (int)MouseScene.Width;
        private const int MaxMessageWidth = 440;
        private const int BottomGap = 56;
        private const int EdgePadding = 14;
        private const int MessageHeadWidth = 40; // the mouse's head left of a message

        private const double MaxOpacity = 0.94; // below 1 also keeps the window layered, which click-through needs
        private const double FadeInMs = 120, FadeOutMs = 200;

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

        private static readonly Color Background = Color.FromArgb(30, 30, 32);

        private readonly Font _font = new("Segoe UI", 10F, FontStyle.Regular);
        private readonly Func<float[], int> _recentAudio;
        private readonly float[] _samples = new float[SpectrumAnalyzer.WindowSize];
        private readonly SpectrumAnalyzer _spectrum = new();
        private readonly MouseScene _scene = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
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
        private double _lastTick;
        private MouseScene.Pose _messagePose = MouseScene.Pose.Calm;

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
            Pasted,
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
            _scene.Reset();
            Present(Mode.Recording, "", hideAfter: null);
        }

        // The grass settles while it starts; the mouse gnaws its wheat only if it takes a moment
        public void ShowTranscribing() => Present(Mode.Transcribing, "", hideAfter: null);

        /// <summary>A wink for the pasted text, then it fades out.</summary>
        public void ShowPasted() => Present(Mode.Pasted, "", hideAfter: TimeSpan.FromSeconds(0.6));

        /// <param name="pose">How the mouse beside the message looks: puzzled when no speech was heard, and so on.</param>
        public void ShowMessage(string message, MouseScene.Pose pose = MouseScene.Pose.Calm)
        {
            _messagePose = pose;
            Present(Mode.Message, message, hideAfter: TimeSpan.FromSeconds(3.5));
        }

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
                int textWidth = TextRenderer.MeasureText(_text, Font).Width + LogicalToDeviceUnits(MessageHeadWidth + 2 * EdgePadding);
                width = Math.Clamp(textWidth, LogicalToDeviceUnits(PillWidth), LogicalToDeviceUnits(MaxMessageWidth));
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
                _spectrum.Update(ReadOnlySpan<float>.Empty); // the voice fades away
            }

            double now = _clock.Elapsed.TotalMilliseconds;
            _scene.Update(Math.Min(100, now - _lastTick), CurrentPose(), _spectrum.Levels, now);
            _lastTick = now;
            Invalidate();
        }

        private MouseScene.Pose CurrentPose() => _mode switch
        {
            Mode.Recording => _live ? MouseScene.Pose.Listening : MouseScene.Pose.Asleep,
            Mode.Transcribing => Stopwatch.GetElapsedTime(_modeStarted).TotalMilliseconds >= ProcessingDelayMs
                ? MouseScene.Pose.Working
                : MouseScene.Pose.Settling,
            Mode.Pasted => MouseScene.Pose.Pasted,
            _ => _messagePose,
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Background);
            if (_mode == Mode.Hidden) return;

            double now = _clock.Elapsed.TotalMilliseconds;
            float scale = ClientSize.Height / MouseScene.Height;
            if (_mode == Mode.Message)
            {
                GraphicsState state = g.Save();
                g.ScaleTransform(scale, scale);
                _scene.DrawHead(g, EdgePadding + 10, MouseScene.Height / 2 + 3, 0.85f, _messagePose, now);
                g.Restore(state);

                int left = LogicalToDeviceUnits(EdgePadding + MessageHeadWidth);
                var bounds = new Rectangle(left, 0, ClientSize.Width - left - LogicalToDeviceUnits(EdgePadding), ClientSize.Height);
                TextRenderer.DrawText(g, _text, Font, bounds, ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                return;
            }

            g.ScaleTransform(scale, scale);
            _scene.Draw(g, CurrentPose(), now, recordingDot: true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                _hideTimer.Dispose();
                _scene.Dispose();
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
