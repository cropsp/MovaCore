using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MovaCore.UI
{
    /// <summary>
    /// What the recording indicator shows: the field mouse from the logo sitting in grass, where the grass is the
    /// equalizer (one tuft per frequency band, growing with the voice). Drawn in logical pixels (<see cref="Width"/> x
    /// <see cref="Height"/>); the caller scales the <see cref="Graphics"/> for the DPI.
    /// </summary>
    internal sealed class MouseScene : IDisposable
    {
        public const float Width = 152, Height = 44;

        private const float LineWidth = 1.6f; // the logo's outline
        private const int Tufts = 13;
        private const int Bands = 9; // the lower bands, where the voice is
        private const float RestHeight = 7, SleepHeight = 6, MaxGrowth = 25, MaxRise = 3;
        private const double PulseMs = 900, PulseStaggerMs = 75;
        private const float MouseX = 60, MouseY = 31;

        // The logo's palette (Resources/mouse_icon.png), and a sleepy grey one
        private static readonly Palette Warm = new(Hex(0xE9A257), Hex(0xFBF4E2), Hex(0xF68C76), Hex(0x1B2A33));
        private static readonly Palette Grey = new(Hex(0xB9B3AB), Hex(0xECE8E1), Hex(0xD8CCC5), Hex(0x46525A));
        private static readonly GrassColors Green = new(Hex(0x6FA553), Hex(0x9FCB6B), Hex(0x2F5626));
        private static readonly GrassColors Withered = new(Hex(0x8F948F), Hex(0xAAB0AA), Hex(0x5D625D));
        private static readonly GrassColors Ripe = new(Hex(0xD39B45), Hex(0xE8BB60), Hex(0x8A5C1C));
        private static readonly Color Wheat = Hex(0xECC565);
        private static readonly Color Red = Hex(0xE5484D);
        private static readonly Color Amber = Hex(0xF0A030);
        private static readonly Color Text = Hex(0xFBF4E2);

        private readonly float[] _grass = new float[Tufts];
        private readonly PointF[] _leaf = new PointF[2 * (LeafSteps + 1) + 1];
        private readonly Dictionary<Color, SolidBrush> _brushes = new();
        private readonly Dictionary<(Color, float), Pen> _pens = new();
        private readonly Font _letterFont = new("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        private float _peek; // the voice's level, slow to fall: how far the mouse rises

        private const int LeafSteps = 8;

        public MouseScene() => Reset();

        public enum Pose
        {
            /// <summary>The microphone is still opening: grey, still grass; the mouse dozes.</summary>
            Asleep,

            /// <summary>Recording: the grass grows with the voice.</summary>
            Listening,

            /// <summary>Transcription has just started: the grass settles.</summary>
            Settling,

            /// <summary>Transcription takes a moment: an amber wave through the grass; the mouse gnaws an ear of wheat.</summary>
            Working,

            /// <summary>The text was pasted: a wink.</summary>
            Pasted,

            /// <summary>No speech was heard: the mouse tilts its head.</summary>
            Puzzled,

            /// <summary>The microphone delivered nothing: a paw at the ear.</summary>
            Straining,

            /// <summary>Any other message.</summary>
            Calm,
        }

        private enum Eye
        {
            Open,
            Closed,
            Wink,
        }

        /// <summary>Short grass and a resting mouse, as at the start of a recording.</summary>
        public void Reset()
        {
            Array.Fill(_grass, SleepHeight);
            _peek = 0;
        }

        /// <summary>Moves the grass and the mouse on by <paramref name="elapsedMs"/>.</summary>
        /// <param name="levels">The equalizer's band levels from 0 to 1, lowest first (at least <see cref="Bands"/>).</param>
        public void Update(double elapsedMs, Pose pose, ReadOnlySpan<float> levels, double timeMs)
        {
            float level = 0;
            if (pose == Pose.Listening)
            {
                for (int i = 0; i < Bands; i++) level += levels[i];
                level /= Bands;
            }
            _peek += (level - _peek) * Approach(elapsedMs, level > _peek ? 90 : 700);

            for (int i = 0; i < Tufts; i++)
            {
                float target = pose switch
                {
                    Pose.Listening => RestHeight + MathF.Pow(BandAt(levels, i), 0.7f) * MaxGrowth,
                    Pose.Working => RestHeight + 9 * Swing(timeMs, i),
                    Pose.Asleep => SleepHeight,
                    _ => RestHeight,
                };
                // Up quickly, down a little slower, like the bars it replaces
                _grass[i] += (target - _grass[i]) * Approach(elapsedMs, target > _grass[i] ? 45 : 120);
            }
        }

        /// <summary>The whole scene: grass, the mouse, and what goes with the pose.</summary>
        public void Draw(Graphics g, Pose pose, double timeMs, bool recordingDot)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool asleep = pose == Pose.Asleep;
            GrassColors grass = asleep ? Withered : pose == Pose.Working ? Ripe : Green;
            Palette pal = asleep ? Grey : Warm;
            float t = (float)timeMs;

            const float left = 11, baseY = Height + 3;
            const float step = (Width - 2 * left) / (Tufts - 1);
            for (int i = 0; i < Tufts; i++)
            {
                float sway = asleep ? 0 : MathF.Sin(t / 900 + i * 0.7f) * 0.06f;
                float x = left + i * step;
                if (i % 4 == 2 && !asleep) WheatEar(g, x + 2, baseY, _grass[i] + 4, 0.08f + sway, grass.Edge);
                Tuft(g, x, baseY, _grass[i], i % 2 == 1 ? 1 : -1, i % 2 == 1 ? grass.Back : grass.Front, grass.Edge, sway);
            }

            // The mouse stays where it is: at most a slight lift with the voice
            float rise = pose == Pose.Listening ? Math.Min(1, _peek * 1.6f) * MaxRise : 0;
            float hop = pose == Pose.Pasted ? MathF.Abs(MathF.Sin(t / 150)) * 2 : 0;
            float hx = MouseX, hy = MouseY - rise - hop;
            Union(g, pal, pal.Body, new RectangleF(hx - 20, hy + 2.5f, 24, 19));
            Fill(g, pal.Belly, hx - 2.5f, hy + 14, 6, 6.5f);
            Head(g, hx, hy, 0.95f, pal, pose, t);
            if (pose == Pose.Working)
            {
                WheatEar(g, hx + 17, hy + 17, 11, -0.5f, Green.Edge);
                Paw(g, hx + 11, hy + 10, 0.95f, pal, 0.3f);
            }
            if (pose == Pose.Straining) Paw(g, hx - 2, hy - 4.5f, 0.95f, pal, -0.9f);

            // A few short tufts in front, at its feet
            float frontSway = asleep ? 0 : MathF.Sin(t / 800) * 0.05f;
            Tuft(g, hx - 15, baseY, 9, -1, grass.Front, grass.Edge, frontSway);
            Tuft(g, hx - 4, baseY, 7, 1, grass.Front, grass.Edge, -frontSway);
            Tuft(g, hx + 7, baseY, 8, 1, grass.Front, grass.Edge, frontSway);

            if (asleep) Zzz(g, hx + 11, hy - 12, t);
            if (pose == Pose.Pasted) Sparkle(g, Width - 28, 14, t);
            if (recordingDot && pose == Pose.Listening)
            {
                int alpha = (int)(255 * (0.65 + 0.35 * Math.Sin(t / 260)));
                Fill(g, Color.FromArgb(alpha, Red), Width - 14, 12, 3, 3);
            }
        }

        /// <summary>The mouse's head alone, for messages; (x, y) is the middle of the head.</summary>
        public void DrawHead(Graphics g, float x, float y, float scale, Pose pose, double timeMs)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Head(g, x, y, scale, Warm, pose, (float)timeMs);
            if (pose == Pose.Straining) Paw(g, x - 2 * scale, y - 4.5f * scale, scale, Warm, -0.9f);
        }

        // ---- The mouse ---------------------------------------------------------------------------------

        // The logo's head, facing right; drawn around (0, 0) at scale 1, about 31 x 28 pixels
        private void Head(Graphics g, float x, float y, float scale, Palette pal, Pose pose, float t)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y);
            if (pose == Pose.Puzzled) g.RotateTransform(-16);
            g.ScaleTransform(scale, scale);

            float droop = pose == Pose.Asleep ? -1 : 0; // the ears turn down, they never grow
            Ear(g, -6.6f, -8.4f, 6.4f, droop, pal);
            Ear(g, 3.4f, -10f, 5.8f, droop * 1.15f, pal);
            Union(g, pal, pal.Body, new RectangleF(-11, -8.5f, 22, 19), new RectangleF(0.4f, -2.4f, 13.2f, 11.2f));
            Fill(g, pal.Belly, 7.9f, 5.3f, 5.4f, 3.6f);
            float chew = pose == Pose.Working ? 0.5f + 0.5f * MathF.Sin(t / 70) : 0;
            if (chew > 0) Fill(g, pal.Belly, 3.4f, 5.2f, 2.5f + chew * 1.5f, 2 + chew * 0.9f);

            Eye eye = pose switch { Pose.Asleep => Eye.Closed, Pose.Pasted => Eye.Wink, _ => Eye.Open };
            DrawEye(g, 3.6f, -1.4f, eye, pal);
            Fill(g, pal.Line, 13.4f, 1.9f, 1.75f, 1.35f);

            Pen line = Pen(pal.Line, LineWidth);
            Quad(g, line, new PointF(10.6f, 5.7f), new PointF(11.8f, 6.7f + chew * 1.3f), new PointF(13, 5.5f));
            float whisk = pose == Pose.Listening ? MathF.Sin(t / 90) * Math.Min(1, _peek * 1.6f) * 1.2f : 0;
            Pen thin = Pen(pal.Line, LineWidth * 0.55f);
            g.DrawLine(thin, 10.4f, 3.3f, 18f, 1.2f + whisk);
            g.DrawLine(thin, 10.7f, 4.3f, 18.3f, 4.7f + whisk * 0.5f);
            g.DrawLine(thin, 10.5f, 5.1f, 17.4f, 7.9f + whisk * 0.3f);
            g.Restore(state);
        }

        private void Ear(Graphics g, float x, float y, float r, float droop, Palette pal)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y - droop * 0.8f);
            g.RotateTransform(-17 * droop);
            Fill(g, pal.Body, 0, 0, r, r, Pen(pal.Line, LineWidth));
            Fill(g, pal.Ear, r * 0.18f, r * 0.12f, r * 0.6f, r * 0.62f);
            g.Restore(state);
        }

        private void DrawEye(Graphics g, float x, float y, Eye eye, Palette pal)
        {
            if (eye == Eye.Open)
            {
                Fill(g, pal.Line, x, y, 1.8f, 1.95f);
                Fill(g, Color.White, x + 0.65f, y - 0.75f, 0.62f, 0.62f);
                return;
            }
            Pen line = Pen(pal.Line, LineWidth);
            if (eye == Eye.Closed)
                Quad(g, line, new PointF(x - 1.9f, y - 0.2f), new PointF(x, y + 1.7f), new PointF(x + 1.9f, y - 0.2f));
            else
                Quad(g, line, new PointF(x - 1.9f, y + 0.7f), new PointF(x, y - 1.6f), new PointF(x + 1.9f, y + 0.7f));
        }

        private void Paw(Graphics g, float x, float y, float scale, Palette pal, float radians)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(radians * 180 / MathF.PI);
            g.ScaleTransform(scale, scale);
            Fill(g, pal.Body, 0, 0, 2.3f, 3, Pen(pal.Line, LineWidth));
            g.Restore(state);
        }

        // Shapes outlined as one: the outlines first at double width, then the fills cover their inner halves
        private void Union(Graphics g, Palette pal, Color fill, params RectangleF[] shapes)
        {
            Pen outline = Pen(pal.Line, LineWidth * 2);
            foreach (RectangleF shape in shapes) g.DrawEllipse(outline, shape);
            SolidBrush brush = Brush(fill);
            foreach (RectangleF shape in shapes) g.FillEllipse(brush, shape);
        }

        // ---- The grass ---------------------------------------------------------------------------------

        // A tuft: a taller leaf and a shorter one bending the other way, from one root
        private void Tuft(Graphics g, float x, float baseY, float height, int side, Color fill, Color edge, float sway)
        {
            Leaf(g, x + 1.2f * side, baseY, height * 0.68f, -side * 0.38f + sway, fill, edge);
            Leaf(g, x, baseY, height, side * 0.22f + sway, fill, edge);
        }

        // A soft leaf of grass: it bends to one side and ends in a rounded tip, not a point
        private void Leaf(Graphics g, float x, float baseY, float height, float bend, Color fill, Color edge)
        {
            float tipX = x + bend * height, tipY = baseY - height * (1 - 0.18f * MathF.Abs(bend));
            float ctrlX = x + bend * height * 0.15f, ctrlY = baseY - height * 0.7f;
            int count = LeafSteps + 1;
            for (int k = 0; k <= LeafSteps; k++)
            {
                float t = (float)k / LeafSteps, u = 1 - t;
                float px = u * u * x + 2 * u * t * ctrlX + t * t * tipX;
                float py = u * u * baseY + 2 * u * t * ctrlY + t * t * tipY;
                // The curve's direction, turned a quarter for the leaf's width
                float dx = 2 * u * (ctrlX - x) + 2 * t * (tipX - ctrlX);
                float dy = 2 * u * (ctrlY - baseY) + 2 * t * (tipY - ctrlY);
                float length = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
                float nx = -dy / length, ny = dx / length;
                float half = (2.6f * (1 - t) + 0.9f * t) / 2;
                _leaf[k] = new PointF(px + nx * half, py + ny * half);
                _leaf[2 * count - k] = new PointF(px - nx * half, py - ny * half);
                if (k == LeafSteps) _leaf[count] = new PointF(px + dx / length * 0.5f, py + dy / length * 0.5f); // the round tip
            }
            g.FillPolygon(Brush(fill), _leaf);
            g.DrawPolygon(Pen(edge, 0.8f), _leaf);
        }

        // An ear of wheat on a thin stem
        private void WheatEar(Graphics g, float x, float baseY, float height, float lean, Color edge)
        {
            float tipX = x + lean * height, tipY = baseY - height;
            Quad(g, Pen(edge, 1), new PointF(x, baseY), new PointF(x + lean * height * 0.2f, baseY - height * 0.6f), new PointF(tipX, tipY));
            Pen outline = Pen(Warm.Line, 0.8f);
            for (int k = 0; k < 4; k++)
            {
                float d = k * 2.6f, gx = tipX - lean * d, gy = tipY + d;
                Grain(g, gx - 1.3f, gy, -29, outline);
                Grain(g, gx + 1.3f, gy, 29, outline);
            }
            Grain(g, tipX, tipY - 1.8f, lean * 57, outline);
        }

        private void Grain(Graphics g, float x, float y, float degrees, Pen outline)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(degrees);
            Fill(g, Wheat, 0, 0, 1.2f, 2, outline);
            g.Restore(state);
        }

        // ---- Extras ------------------------------------------------------------------------------------

        private void Zzz(Graphics g, float x, float y, float t)
        {
            for (int k = 0; k < 3; k++)
            {
                float p = (t / 1500 + k / 3f) % 1;
                GraphicsState state = g.Save();
                g.TranslateTransform(x + p * 7, y - p * 9);
                g.ScaleTransform(0.75f + p * 0.5f, 0.75f + p * 0.5f);
                g.DrawString("z", _letterFont, Brush(Color.FromArgb((int)((1 - p) * 230), Text)), 0, -8);
                g.Restore(state);
            }
        }

        private void Sparkle(Graphics g, float x, float y, float t)
        {
            float size = 3 + 1.2f * MathF.Sin(t / 120);
            float turn = t / 900;
            Span<PointF> points = stackalloc PointF[8];
            for (int i = 0; i < 8; i++)
            {
                float r = i % 2 == 0 ? size : size * 0.32f;
                float a = turn + i / 8f * MathF.Tau;
                points[i] = new PointF(x + MathF.Cos(a) * r, y + MathF.Sin(a) * r);
            }
            g.FillPolygon(Brush(Amber), points.ToArray());
        }

        // ---- Drawing helpers ---------------------------------------------------------------------------

        private void Fill(Graphics g, Color color, float x, float y, float rx, float ry, Pen? outline = null)
        {
            g.FillEllipse(Brush(color), x - rx, y - ry, rx * 2, ry * 2);
            if (outline != null) g.DrawEllipse(outline, x - rx, y - ry, rx * 2, ry * 2);
        }

        // A quadratic curve as GDI+'s cubic Bézier
        private static void Quad(Graphics g, Pen pen, PointF from, PointF control, PointF to)
        {
            var c1 = new PointF(from.X + 2f / 3 * (control.X - from.X), from.Y + 2f / 3 * (control.Y - from.Y));
            var c2 = new PointF(to.X + 2f / 3 * (control.X - to.X), to.Y + 2f / 3 * (control.Y - to.Y));
            g.DrawBezier(pen, from, c1, c2, to);
        }

        private SolidBrush Brush(Color color)
        {
            if (!_brushes.TryGetValue(color, out SolidBrush? brush)) _brushes[color] = brush = new SolidBrush(color);
            return brush;
        }

        private Pen Pen(Color color, float width)
        {
            if (!_pens.TryGetValue((color, width), out Pen? pen))
            {
                pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                _pens[(color, width)] = pen;
            }
            return pen;
        }

        // The level of tuft i, between the bands on either side of it
        private static float BandAt(ReadOnlySpan<float> levels, int tuft)
        {
            float position = tuft * (Bands - 1f) / (Tufts - 1);
            int low = (int)position, high = Math.Min(Bands - 1, low + 1);
            return levels[low] + (levels[high] - levels[low]) * (position - low);
        }

        // The wave through the grass: from the outer tufts to the middle, as in Handy's indicator
        private static float Swing(double timeMs, int tuft)
        {
            const float middle = (Tufts - 1) / 2f;
            double delay = (middle - Math.Abs(tuft - middle)) * PulseStaggerMs;
            double phase = ((timeMs - delay) % PulseMs + PulseMs) % PulseMs / PulseMs;
            return (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
        }

        private static float Approach(double elapsedMs, double timeConstantMs) => (float)(1 - Math.Exp(-elapsedMs / timeConstantMs));

        private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        public void Dispose()
        {
            foreach (SolidBrush brush in _brushes.Values) brush.Dispose();
            foreach (Pen pen in _pens.Values) pen.Dispose();
            _brushes.Clear();
            _pens.Clear();
            _letterFont.Dispose();
        }

        private readonly record struct Palette(Color Body, Color Belly, Color Ear, Color Line);

        private readonly record struct GrassColors(Color Back, Color Front, Color Edge);
    }
}
