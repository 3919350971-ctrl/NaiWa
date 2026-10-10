using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace Naiwa
{
    internal static class Theme
    {
        internal static readonly Color Cream = Color.FromArgb(255, 252, 242);
        internal static readonly Color Ink = Color.FromArgb(64, 66, 42);
        internal static readonly Color Muted = Color.FromArgb(118, 121, 102);
        internal static readonly Color Green = Color.FromArgb(113, 144, 106);
        internal static readonly Color Yellow = Color.FromArgb(250, 208, 91);
        internal static readonly Color Line = Color.FromArgb(229, 231, 216);
        internal static GraphicsPath Round(RectangleF bounds, float radius)
        {
            float d = radius * 2; GraphicsPath p = new GraphicsPath();
            p.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            p.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            p.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            p.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static void FillRound(Graphics g, Color color, RectangleF bounds, float radius)
        { using (GraphicsPath path = Round(bounds, radius)) using (Brush b = new SolidBrush(color)) g.FillPath(b, path); }
    }

    internal sealed class SpriteLibrary : IDisposable
    {
        private readonly Dictionary<string, Bitmap> sprites = new Dictionary<string, Bitmap>();
        private readonly List<Bitmap> laughs = new List<Bitmap>();
        internal const int LaughFrameCount = 12;
        internal const double LaughDuration = 2.0;
        internal readonly Icon Icon;
        internal static readonly string[] Names = { "idle", "blink", "pat", "belly", "drag", "land", "sit", "sleep", "wake", "look-left", "look-right", "walk" };
        internal SpriteLibrary()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (string name in Names)
            using (Stream stream = assembly.GetManifestResourceStream("Naiwa.assets." + name + ".png"))
            {
                if (stream == null) throw new InvalidDataException("缺少角色资源：" + name);
                using (Bitmap original = new Bitmap(stream)) sprites[name] = new Bitmap(original);
            }
            for (int i = 0; i < LaughFrameCount; i++)
            using (Stream stream = assembly.GetManifestResourceStream("Naiwa.assets.belly-" + i.ToString("00") + ".png"))
            {
                if (stream == null) throw new InvalidDataException("缺少捧腹大笑帧：" + i);
                using (Bitmap original = new Bitmap(stream)) laughs.Add(new Bitmap(original));
            }
            using (Stream stream = assembly.GetManifestResourceStream("Naiwa.assets.naiwa.ico"))
                Icon = new Icon(stream);
        }
        internal Bitmap Get(string name) { return sprites.ContainsKey(name) ? sprites[name] : sprites["idle"]; }
        internal static int LaughIndex(double elapsed, bool reduced)
        { return reduced ? 0 : (int)(Math.Max(0, elapsed) * 12) % LaughFrameCount; }
        internal Bitmap GetFrame(string name, double elapsed, bool reduced)
        { return name == "belly" ? laughs[LaughIndex(elapsed, reduced)] : Get(name); }
        internal void Draw(Graphics g, string name, RectangleF rect, float alpha)
        { Draw(g, name, rect, alpha, 0, false); }
        internal void Draw(Graphics g, string name, RectangleF rect, float alpha, double elapsed, bool reduced)
        {
            if (alpha <= 0) return;
            Bitmap sprite = GetFrame(name, elapsed, reduced);
            if (alpha >= .999f) { g.DrawImage(sprite, rect); return; }
            using (ImageAttributes a = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = Math.Min(1, alpha); a.SetColorMatrix(matrix);
                g.DrawImage(sprite, Rectangle.Round(rect), 0, 0, sprite.Width, sprite.Height, GraphicsUnit.Pixel, a);
            }
        }
        public void Dispose()
        { foreach (Bitmap b in sprites.Values) b.Dispose(); foreach (Bitmap b in laughs) b.Dispose(); Icon.Dispose(); }
    }

    internal sealed class PetRenderer
    {
        private readonly SpriteLibrary sprites;
        internal RectangleF Body;
        internal Rectangle ProgressArea;
        internal PetRenderer(SpriteLibrary library) { sprites = library; }
        internal Bitmap Render(Size size, float scale, int height, string pose, string basePose,
            double elapsed, double duration, string bubble, bool focus, bool paused, double progress,
            string clock, bool reduced)
        {
            Bitmap canvas = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float frameH = height * scale * 440f / 360, frameW = frameH * 400f / 440;
                float bottom = size.Height - 25 * scale;
                float motion = reduced ? 0 : (float)Math.Sin(elapsed * 1.75) * 1.25f * scale;
                if (pose == "belly") motion = 0; // The supplied frames already animate the body.
                float sx = 1, sy = 1, dy = 0, angle = 0;
                if (!reduced)
                {
                    if (pose == "land") { float pulse = (float)Math.Sin(elapsed * 17) * (float)Math.Exp(-elapsed * 5); sx += .08f * pulse; sy -= .08f * pulse; }
                    else if (pose == "drag") { angle = (float)Math.Sin(elapsed * 4) * 3; dy = -4 * scale; }
                    else if (pose == "pat") angle = (float)Math.Sin(Math.Min(elapsed, 1) * Math.PI) * -5;
                    else if (pose == "walk") dy = -(float)Math.Abs(Math.Sin(elapsed * 6)) * 3 * scale;
                }
                Body = new RectangleF((size.Width - frameW) / 2, bottom - frameH, frameW, frameH);
                RectangleF animated = new RectangleF(size.Width / 2f - frameW * sx / 2,
                    bottom - frameH * sy + motion + dy, frameW * sx, frameH * sy);
                GraphicsState saved = g.Save();
                g.TranslateTransform(size.Width / 2f, bottom - frameH / 2); g.RotateTransform(angle);
                g.TranslateTransform(-size.Width / 2f, -(bottom - frameH / 2));
                // Short crossfades prevent abrupt pose switches. Drag remains immediate.
                float blend = 1;
                if (pose != basePose && pose != "drag" && pose != "blink" && duration > 0)
                    blend = (float)Math.Min(1, Math.Min(elapsed / .12, (duration - elapsed) / .18));
                blend = Math.Max(0, blend);
                if (blend < 1) sprites.Draw(g, basePose, animated, 1 - blend);
                sprites.Draw(g, pose, animated, blend, elapsed, reduced); g.Restore(saved);
                if (pose == "sleep")
                {
                    // Keep the sleep indicator above/right of the head, inside the transparent canvas.
                    float drift = reduced ? 0 : (float)Math.Sin(elapsed * 1.6) * 2.5f * scale;
                    for (int i = 0; i < 3; i++)
                    using (Font z = new Font("Segoe UI", (12 + i * 4) * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush ink = new SolidBrush(Color.FromArgb(230 - i * 25, Theme.Green)))
                    {
                        float x = Body.Left + Body.Width * .65f + i * 15 * scale;
                        float y = Body.Top + Body.Height * .21f - i * 20 * scale + drift;
                        g.DrawString("Z", z, ink, x, y);
                    }
                }
                if (!String.IsNullOrEmpty(bubble))
                {
                    RectangleF rect = new RectangleF(8 * scale, 9 * scale, size.Width - 16 * scale, 53 * scale);
                    Theme.FillRound(g, Theme.Cream, rect, 14 * scale);
                    using (Brush b = new SolidBrush(Theme.Cream))
                        g.FillPolygon(b, new PointF[] { new PointF(size.Width / 2f - 6 * scale, rect.Bottom - 1),
                            new PointF(size.Width / 2f + 7 * scale, rect.Bottom - 1), new PointF(size.Width / 2f, rect.Bottom + 7 * scale) });
                    using (Font f = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(Theme.Ink))
                    using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(bubble, f, b, new RectangleF(rect.X + 7 * scale, rect.Y + 3 * scale, rect.Width - 14 * scale, rect.Height - 6 * scale), format);
                }
                ProgressArea = Rectangle.Empty;
                if (focus)
                {
                    int w = (int)(105 * scale), h = (int)(20 * scale);
                    ProgressArea = new Rectangle((size.Width - w) / 2, size.Height - h - (int)(2 * scale), w, h);
                    Theme.FillRound(g, Theme.Cream, ProgressArea, 9 * scale);
                    using (Font f = new Font("Segoe UI", 10 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(Theme.Ink))
                        g.DrawString((paused ? "Ⅱ " : "● ") + clock, f, b, ProgressArea.Left + 9 * scale, ProgressArea.Top + 2 * scale);
                    RectangleF track = new RectangleF(ProgressArea.Right - 40 * scale, ProgressArea.Top + 8 * scale, 29 * scale, 4 * scale);
                    Theme.FillRound(g, Theme.Line, track, 1.9f * scale);
                    if (progress > .01) Theme.FillRound(g, Theme.Green,
                        new RectangleF(track.X, track.Y, Math.Max(4 * scale, track.Width * (float)progress), track.Height), 1.9f * scale);
                }
            }
            return canvas;
        }
    }
}
