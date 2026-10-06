using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
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
    /// lets clicks through. A layered window with per-pixel alpha (UpdateLayeredWindow) gives it the shape of a real
    /// capsule with smooth edges and a soft shadow, the same on Windows 10 and 11; WinForms painting and the Opacity
    /// property are not used, since SetLayeredWindowAttributes would stop UpdateLayeredWindow from working.
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
        private const int ShadowMargin = 6; // room around the capsule for its shadow
        private const float TextSize = 13.3f; // 10 pt

        private const double MaxOpacity = 0.94;
        private const double FadeInMs = 120, FadeOutMs = 200;

        /// <summary>A quick transcription shows no animation at all, only a slower one does.</summary>
        private const double ProcessingDelayMs = 300;

        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        private const uint ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00, AC_SRC_ALPHA = 0x01;

        private static readonly Color Background = Color.FromArgb(30, 30, 32);
        private static readonly Color Edge = Color.FromArgb(18, 255, 255, 255);
        private static readonly Color TextColor = Color.FromArgb(251, 244, 226);

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
        private double _opacity; // of the whole window, faded in and out
        private MouseScene.Pose _messagePose = MouseScene.Pose.Calm;
        private Font? _textFont; // for the current DPI
        private int _textFontDpi;
        private bool _layeringFailed;

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
            Size = new Size(PillWidth + 2 * ShadowMargin, PillHeight + 2 * ShadowMargin);

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
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED;
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
                _opacity = 0;
                Show(); // without activation (see ShowWithoutActivation), and invisible until the first frame
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                StartFade(fadeOut: false);
            }
            else if (_fadingOut)
            {
                StartFade(fadeOut: false); // shown again while it was fading out
            }
            Relayout();

            _animation.Start();
            Render();
        }

        private void StartFade(bool fadeOut)
        {
            _fadingOut = fadeOut;
            _fadeFrom = _opacity;
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
                _opacity = _fadeFrom * (1 - progress);
            }
            else if (_opacity < MaxOpacity)
            {
                double progress = Math.Min(1, elapsed / FadeInMs);
                _opacity = _fadeFrom + (MaxOpacity - _fadeFrom) * progress;
            }
        }

        // The capsule at the bottom center of the working area, with room around it for the shadow; a message gets
        // as wide as its text needs
        private void Relayout()
        {
            int width = LogicalToDeviceUnits(PillWidth);
            if (_mode == Mode.Message)
            {
                using var measuring = new Bitmap(1, 1);
                using Graphics g = Graphics.FromImage(measuring);
                int textWidth = (int)Math.Ceiling(g.MeasureString(_text, TextFont()).Width) +
                    LogicalToDeviceUnits(MessageHeadWidth + 2 * EdgePadding);
                width = Math.Clamp(textWidth, LogicalToDeviceUnits(PillWidth), LogicalToDeviceUnits(MaxMessageWidth));
            }
            int height = LogicalToDeviceUnits(PillHeight);
            int margin = LogicalToDeviceUnits(ShadowMargin);
            Bounds = new Rectangle(
                _area.Left + (_area.Width - width) / 2 - margin,
                _area.Bottom - LogicalToDeviceUnits(BottomGap) - height - margin,
                width + 2 * margin,
                height + 2 * margin);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (_mode == Mode.Hidden) return;
            Relayout();
            Render();
        }

        // GDI+ text in pixels: GDI's TextRenderer would draw transparent text into a frame with alpha
        private Font TextFont()
        {
            if (_textFont == null || _textFontDpi != DeviceDpi)
            {
                _textFont?.Dispose();
                _textFont = new Font("Segoe UI", TextSize * DeviceDpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
                _textFontDpi = DeviceDpi;
            }
            return _textFont;
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            UpdateFade();
            if (!Visible) return;
            if (_mode == Mode.Message && !_fadingOut && _opacity >= MaxOpacity)
            {
                _animation.Stop(); // a message does not move
                Render();
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
            Render();
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

        /// <summary>The window's current picture: the capsule and its shadow on a transparent background.</summary>
        internal Bitmap RenderFrame()
        {
            var frame = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
            using Graphics g = Graphics.FromImage(frame);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            if (_mode == Mode.Hidden) return frame;

            float scale = DeviceDpi / 96f;
            float margin = LogicalToDeviceUnits(ShadowMargin);
            var pill = new RectangleF(margin, margin, Width - 2 * margin, Height - 2 * margin);

            // A soft shadow: a few larger, fainter capsules a little lower
            for (int i = 4; i >= 1; i--)
            {
                RectangleF spread = pill;
                spread.Inflate(i * scale, i * scale);
                spread.Offset(0, 1.5f * scale);
                using GraphicsPath shadow = Capsule(spread);
                using var shade = new SolidBrush(Color.FromArgb(8 + (4 - i) * 7, 0, 0, 0));
                g.FillPath(shade, shadow);
            }

            using GraphicsPath capsule = Capsule(pill);
            using (var fill = new SolidBrush(Background)) g.FillPath(fill, capsule);
            using (var edge = new Pen(Edge, scale)) g.DrawPath(edge, capsule);
            g.SetClip(capsule);

            double now = _clock.Elapsed.TotalMilliseconds;
            float sceneScale = pill.Height / MouseScene.Height;
            g.TranslateTransform(pill.X, pill.Y);
            g.ScaleTransform(sceneScale, sceneScale);
            if (_mode != Mode.Message)
            {
                _scene.Draw(g, CurrentPose(), now, recordingDot: true);
                return frame;
            }

            _scene.DrawHead(g, EdgePadding + 10, MouseScene.Height / 2 + 3, 0.85f, _messagePose, now);
            g.ResetTransform();
            float left = pill.X + LogicalToDeviceUnits(EdgePadding + MessageHeadWidth);
            var bounds = new RectangleF(left, pill.Y, pill.Right - LogicalToDeviceUnits(EdgePadding) - left, pill.Height);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var format = new StringFormat(StringFormatFlags.NoWrap)
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            using var text = new SolidBrush(TextColor);
            g.DrawString(_text, TextFont(), text, bounds, format);
            return frame;
        }

        private static GraphicsPath Capsule(RectangleF r)
        {
            var path = new GraphicsPath();
            float d = Math.Min(r.Height, r.Width);
            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }

        // Hands the frame to the window: its alpha shapes the window, and the fade sets the overall opacity
        private void Render()
        {
            if (!IsHandleCreated || !Visible) return;
            using Bitmap frame = RenderFrame();
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = frame.GetHbitmap(Color.FromArgb(0));
            IntPtr previous = SelectObject(memory, bitmap);
            try
            {
                var size = new NativeSize { Width = frame.Width, Height = frame.Height };
                var source = new NativePoint();
                var position = new NativePoint { X = Left, Y = Top };
                var blend = new BlendFunction
                {
                    BlendOp = AC_SRC_OVER,
                    SourceConstantAlpha = (byte)Math.Round(255 * Math.Clamp(_opacity, 0, 1)),
                    AlphaFormat = AC_SRC_ALPHA,
                };
                if (!UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref source, 0, ref blend, ULW_ALPHA)
                    && !_layeringFailed)
                {
                    _layeringFailed = true; // once: every frame would fail the same way
                    AppLog.Error($"The recording indicator could not be drawn (error {Marshal.GetLastPInvokeError()})");
                }
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(bitmap);
                DeleteDC(memory);
                _ = ReleaseDC(IntPtr.Zero, screen);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                _hideTimer.Dispose();
                _scene.Dispose();
                _textFont?.Dispose();
            }
            base.Dispose(disposing);
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
            IntPtr hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, uint dwFlags);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetDC(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteDC(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(IntPtr ho);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }
    }
}
