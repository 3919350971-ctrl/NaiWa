using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Build-time only: slice generated atlas into aligned runtime assets, without redrawing the character.
internal static class AssetPack
{
    private static readonly string[] Names = { "idle", "blink", "pat", "belly", "drag", "land", "sit", "sleep", "wake", "look-left", "look-right", "walk" };
    internal static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--laugh") return LaughPack.Run(args[1], args[2]);
        if (args.Length != 2) return 2;
        Directory.CreateDirectory(args[1]);
        using (Bitmap atlas = new Bitmap(args[0]))
        {
            int cellW = atlas.Width / 4, cellH = atlas.Height / 3;
            Rectangle[] bounds = new Rectangle[12];
            for (int i = 0; i < 12; i++)
            {
                int ox = i % 4 * cellW, oy = i / 4 * cellH;
                int left = cellW, right = -1, top = cellH, bottom = -1, largest = 0;
                bool[] visible = new bool[cellW * cellH], visited = new bool[cellW * cellH];
                int[] queue = new int[visible.Length];
                for (int y = 0; y < cellH; y++) for (int x = 0; x < cellW; x++)
                    visible[y * cellW + x] = atlas.GetPixel(ox + x, oy + y).A > 20;
                // Atlas import: ignore isolated fragments from neighbouring cells.
                for (int seed = 0; seed < visible.Length; seed++)
                {
                    if (!visible[seed] || visited[seed]) continue;
                    int head = 0, tail = 1, l = cellW, r = -1, t = cellH, b = -1;
                    queue[0] = seed; visited[seed] = true;
                    while (head < tail)
                    {
                        int n = queue[head++], x = n % cellW, y = n / cellW;
                        l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); b = Math.Max(b, y);
                        int[] neighbours = { x > 0 ? n - 1 : -1, x + 1 < cellW ? n + 1 : -1,
                            y > 0 ? n - cellW : -1, y + 1 < cellH ? n + cellW : -1 };
                        foreach (int next in neighbours) if (next >= 0 && visible[next] && !visited[next])
                        { visited[next] = true; queue[tail++] = next; }
                    }
                    if (tail > largest) { largest = tail; left = l; right = r; top = t; bottom = b; }
                }
                if (right < left) throw new InvalidDataException("Empty cell: " + i);
                bounds[i] = new Rectangle(ox + left, oy + top, right - left + 1, bottom - top + 1);
            }
            float scale = 360f / bounds[0].Height;
            for (int i = 0; i < 12; i++)
            using (Bitmap output = new Bitmap(400, 440, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(output))
            {
                g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                Rectangle source = bounds[i];
                float w = source.Width * scale, h = source.Height * scale;
                float bottom = i == 4 ? 393 : 414;
                g.DrawImage(atlas, new RectangleF((400 - w) / 2, bottom - h, w, h), source, GraphicsUnit.Pixel);
                output.Save(System.IO.Path.Combine(args[1], Names[i] + ".png"), ImageFormat.Png);
                Console.WriteLine(Names[i] + " " + source + " -> " + w.ToString("F1") + "x" + h.ToString("F1"));
            }
        }
        using (Bitmap sprite = new Bitmap(System.IO.Path.Combine(args[1], "idle.png")))
        using (Bitmap icon = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(icon))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(sprite, new Rectangle(3, 1, 58, 64));
            using (MemoryStream png = new MemoryStream())
            {
                icon.Save(png, ImageFormat.Png); byte[] data = png.ToArray();
                using (BinaryWriter writer = new BinaryWriter(File.Create(System.IO.Path.Combine(args[1], "naiwa.ico"))))
                {
                    writer.Write((short)0); writer.Write((short)1); writer.Write((short)1);
                    writer.Write((byte)64); writer.Write((byte)64); writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((short)1); writer.Write((short)32); writer.Write(data.Length); writer.Write(22); writer.Write(data);
                }
            }
        }
        return 0;
    }
}

// Deterministic import of the supplied transparent 4 x 3 laughing sheet.
// Use one scale for every frame; align feet, never resize each pose independently.
internal static class LaughPack
{
    private static Rectangle CharacterBounds(Bitmap image, Rectangle cell)
    {
        int w = cell.Width, h = cell.Height, largest = 0;
        bool[] mask = new bool[w * h], visited = new bool[w * h]; int[] queue = new int[w * h];
        bool[] component = new bool[w * h];
        Rectangle result = Rectangle.Empty;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) mask[y * w + x] = image.GetPixel(cell.X + x, cell.Y + y).A > 20;
        for (int seed = 0; seed < mask.Length; seed++)
        {
            if (!mask[seed] || visited[seed]) continue;
            int head = 0, tail = 1, left = w, right = 0, top = h, bottom = 0;
            queue[0] = seed; visited[seed] = true;
            while (head < tail)
            {
                int n = queue[head++], x = n % w, y = n / w;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                foreach (int next in new int[] { x > 0 ? n - 1 : -1, x + 1 < w ? n + 1 : -1, y > 0 ? n - w : -1, y + 1 < h ? n + w : -1 })
                    if (next >= 0 && mask[next] && !visited[next]) { visited[next] = true; queue[tail++] = next; }
            }
            if (tail > largest)
            {
                largest = tail; Array.Clear(component, 0, component.Length);
                for (int i = 0; i < tail; i++) component[queue[i]] = true;
                result = new Rectangle(cell.X + left, cell.Y + top, right - left + 1, bottom - top + 1);
            }
        }
        if (largest < 1000) throw new InvalidDataException("Missing laughing character in cell " + cell);
        // Drop detached near-transparent background specks, retaining a one-pixel
        // antialias fringe around the character's connected alpha mask.
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            bool keep = component[y * w + x];
            for (int dy = -1; !keep && dy <= 1; dy++) for (int dx = -1; !keep && dx <= 1; dx++)
                if (x + dx >= 0 && x + dx < w && y + dy >= 0 && y + dy < h) keep = component[(y + dy) * w + x + dx];
            if (!keep || image.GetPixel(cell.X + x, cell.Y + y).A <= 6) image.SetPixel(cell.X + x, cell.Y + y, Color.Transparent);
        }
        return result;
    }
    internal static int Run(string input, string directory)
    {
        Directory.CreateDirectory(directory);
        using (Bitmap atlas = new Bitmap(input))
        {
            if (atlas.GetPixel(0, 0).A > 0) throw new InvalidDataException("Laughing atlas must have a transparent background");
            Rectangle[] bounds = new Rectangle[12]; int maxHeight = 0, maxWidth = 0;
            for (int i = 0; i < 12; i++)
            {
                int col = i % 4, row = i / 4;
                Rectangle cell = Rectangle.FromLTRB(col * atlas.Width / 4, row * atlas.Height / 3, (col + 1) * atlas.Width / 4, (row + 1) * atlas.Height / 3);
                bounds[i] = CharacterBounds(atlas, cell);
                maxHeight = Math.Max(maxHeight, bounds[i].Height); maxWidth = Math.Max(maxWidth, bounds[i].Width);
            }
            float scale = Math.Min(360f / maxHeight, 350f / maxWidth);
            for (int i = 0; i < 12; i++)
            using (Bitmap frame = new Bitmap(400, 440, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Rectangle source = bounds[i]; float w = source.Width * scale, h = source.Height * scale;
                g.DrawImage(atlas, new RectangleF((400 - w) / 2, 414 - h, w, h), source, GraphicsUnit.Pixel);
                string path = System.IO.Path.Combine(directory, "belly-" + i.ToString("00") + ".png"); frame.Save(path, ImageFormat.Png);
                if (i == 0) frame.Save(System.IO.Path.Combine(directory, "belly.png"), ImageFormat.Png);
                Console.WriteLine("laugh " + i + " " + source + " scale=" + scale.ToString("F4"));
            }
        }
        return 0;
    }
}
