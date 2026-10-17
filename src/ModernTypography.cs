using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VramMonitorPortable
{
    internal static class ModernTypography
    {
        internal static Font Create(float size, bool emphasis, GraphicsUnit unit)
        {
            // Windows-standard faces; no bundled or separately installed font is needed.
            return new Font(emphasis ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular, unit);
        }

        internal static GraphicsPath TextPath(string text, int pixels)
        {
            GraphicsPath path = new GraphicsPath();
            using (Font font = Create(pixels, true, GraphicsUnit.Pixel))
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
                path.AddString(text, font.FontFamily, (int)font.Style, pixels, PointF.Empty, format);
            return path;
        }

        internal static Size MeasureOverlay(string text, int pixels, int rotation)
        {
            if (string.IsNullOrEmpty(text)) return Size.Empty;
            using (GraphicsPath path = TextPath(text, pixels))
            {
                RectangleF bounds = path.GetBounds();
                int width = Math.Max(1, (int)Math.Ceiling(bounds.Width) + 2);
                int height = Math.Max(1, (int)Math.Ceiling(bounds.Height) + 2);
                return rotation == 90 || rotation == 270 ? new Size(height, width) : new Size(width, height);
            }
        }

        internal static Bitmap RenderOverlay(string text, int pixels, Color color, int rotation)
        {
            using (GraphicsPath path = TextPath(text, pixels))
            {
                RectangleF bounds = path.GetBounds();
                int width = Math.Max(1, (int)Math.Ceiling(bounds.Width) + 2);
                int height = Math.Max(1, (int)Math.Ceiling(bounds.Height) + 2);
                using (Matrix position = new Matrix())
                {
                    position.Translate(1 - bounds.Left, 1 - bounds.Top);
                    path.Transform(position);
                }
                return RenderPath(path, new Size(width, height), color, rotation);
            }
        }

        internal static Bitmap RenderPath(GraphicsPath path, Size size, Color color, int rotation)
        {
            const int scale = 4;
            int width = size.Width, height = size.Height;
            using (Bitmap large = new Bitmap(width * scale, height * scale, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(large))
                using (Brush brush = new SolidBrush(Color.White))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.ScaleTransform(scale, scale);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.FillPath(brush, path);
                }
                Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(result))
                    using (ImageAttributes attributes = new ImageAttributes())
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        graphics.DrawImage(large, new Rectangle(0, 0, width, height),
                            0, 0, large.Width, large.Height, GraphicsUnit.Pixel, attributes);
                    }
                    ApplyGlyphColor(result, color);
                    if (rotation == 90) result.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    else if (rotation == 270) result.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    return result;
                }
                catch { result.Dispose(); throw; }
            }
        }

        private static void ApplyGlyphColor(Bitmap bitmap, Color color)
        {
            // Resample coverage only, then premultiply the exact chosen color. GDI+ bicubic
            // filtering can otherwise overshoot RGB independently of alpha at glyph edges.
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size),
                ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                byte[] row = new byte[bitmap.Width * 4];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr address = IntPtr.Add(data.Scan0, y * data.Stride);
                    Marshal.Copy(address, row, 0, row.Length);
                    for (int x = 0; x < row.Length; x += 4)
                    {
                        int alpha = (row[x + 3] * color.A + 127) / 255;
                        row[x] = (byte)((color.B * alpha + 127) / 255);
                        row[x + 1] = (byte)((color.G * alpha + 127) / 255);
                        row[x + 2] = (byte)((color.R * alpha + 127) / 255);
                        row[x + 3] = (byte)alpha;
                    }
                    Marshal.Copy(row, 0, address, row.Length);
                }
            }
            finally { bitmap.UnlockBits(data); }
        }
    }


    // Fixed tabular cells and fixed canvas bounds; the chosen Segoe glyphs stay unchanged.
    // This layout is reused until size/spacing/visibility changes, not rebuilt each sample.
    internal sealed class OverlayMetricLayout : IDisposable
    {
        private const string Characters = "0123456789-wc";
        private const int PowerDigits = 4, TemperatureDigits = 3;
        private readonly GraphicsPath[] glyphs = new GraphicsPath[Characters.Length];
        private readonly RectangleF[] bounds = new RectangleF[Characters.Length];
        private readonly bool showPower, showTemperature;
        private readonly int digitAdvance, unitGap;
        private readonly float top;
        private readonly Point powerOrigin, temperatureOrigin;
        private readonly Size canvas;

        internal OverlayMetricLayout(int pixels, bool power, bool temperature, int spacing)
        {
            showPower = power;
            showTemperature = temperature;
            pixels = Math.Max(8, Math.Min(288, pixels));
            spacing = Math.Max(-20, Math.Min(20, spacing));
            try
            {
                float maximumDigitWidth = 0, bottom = 0, minimumTop = float.MaxValue;
                for (int i = 0; i < Characters.Length; i++)
                {
                    glyphs[i] = ModernTypography.TextPath(Characters[i].ToString(), pixels);
                    bounds[i] = glyphs[i].GetBounds();
                    minimumTop = Math.Min(minimumTop, bounds[i].Top);
                    bottom = Math.Max(bottom, bounds[i].Bottom);
                    if (i <= 10) maximumDigitWidth = Math.Max(maximumDigitWidth, bounds[i].Width);
                }
                top = minimumTop;
                float fontAdvance, spaceAdvance;
                using (GraphicsPath pair = ModernTypography.TextPath("00", pixels))
                using (GraphicsPath spacedPair = ModernTypography.TextPath("0 0", pixels))
                {
                    fontAdvance = pair.GetBounds().Width - bounds[0].Width;
                    spaceAdvance = spacedPair.GetBounds().Width - pair.GetBounds().Width;
                }
                digitAdvance = (int)Math.Ceiling(Math.Max(fontAdvance, maximumDigitWidth + pixels * 0.04f));
                unitGap = Math.Max(1, (int)Math.Round(pixels / 24d));
                int powerWidth = PowerDigits * digitAdvance + unitGap + (int)Math.Ceiling(bounds[11].Width);
                int temperatureWidth = TemperatureDigits * digitAdvance + unitGap + (int)Math.Ceiling(bounds[12].Width);
                int height = (int)Math.Ceiling(bottom - top);
                int width = power ? powerWidth : temperature ? temperatureWidth : 0;
                powerOrigin = new Point(1, 1);
                temperatureOrigin = new Point(1, 1);
                if (power && temperature)
                {
                    if (spacing < 0)
                    {
                        int lineHeight;
                        using (Font font = ModernTypography.Create(pixels, true, GraphicsUnit.Pixel))
                            lineHeight = (int)Math.Ceiling(pixels * (double)font.FontFamily.GetLineSpacing(font.Style)
                                / font.FontFamily.GetEmHeight(font.Style));
                        width = Math.Max(powerWidth, temperatureWidth);
                        powerOrigin = new Point(1 + width - powerWidth, 1);
                        temperatureOrigin = new Point(1 + width - temperatureWidth, 1 + lineHeight * -spacing);
                        height += lineHeight * -spacing;
                    }
                    else
                    {
                        int gap = (int)Math.Round(spaceAdvance * spacing);
                        temperatureOrigin = new Point(1 + powerWidth + gap, 1);
                        width = powerWidth + gap + temperatureWidth;
                    }
                }
                canvas = power || temperature ? new Size(width + 2, height + 2) : Size.Empty;
            }
            catch { Dispose(); throw; }
        }

        internal Size SizeAtRotation(int rotation)
        {
            return rotation == 90 || rotation == 270 ? new Size(canvas.Height, canvas.Width) : canvas;
        }

        internal Bitmap Render(int power, int temperature, Color color, int rotation)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                if (showPower) AppendMetric(path, power, PowerDigits, 'w', powerOrigin);
                if (showTemperature) AppendMetric(path, temperature, TemperatureDigits, 'c', temperatureOrigin);
                return ModernTypography.RenderPath(path,
                    canvas.IsEmpty ? new Size(1, 1) : canvas, color, rotation);
            }
        }

        private void AppendMetric(GraphicsPath target, int value, int digits, char unit, Point origin)
        {
            int maximum = digits == PowerDigits ? 9999 : 999;
            string text = value < 0 || value > maximum ? "--" :
                value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int start = digits - text.Length;
            for (int i = 0; i < text.Length; i++)
            {
                int index = Characters.IndexOf(text[i]);
                // Center each unchanged glyph in an equal-width cell; retain one common baseline.
                AppendGlyph(target, index, origin.X + (start + i) * digitAdvance
                    + (digitAdvance - bounds[index].Width) / 2f, origin.Y);
            }
            AppendGlyph(target, Characters.IndexOf(unit), origin.X + digits * digitAdvance + unitGap, origin.Y);
        }

        private void AppendGlyph(GraphicsPath target, int index, float x, float y)
        {
            using (GraphicsPath glyph = (GraphicsPath)glyphs[index].Clone())
            using (Matrix position = new Matrix())
            {
                position.Translate(x - bounds[index].Left, y - top);
                glyph.Transform(position);
                target.AddPath(glyph, false);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < glyphs.Length; i++)
                if (glyphs[i] != null) { glyphs[i].Dispose(); glyphs[i] = null; }
        }
    }

    // Proportional modern font, but fixed column ends for %, W and temperature.
    internal sealed class TelemetryLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            string[] parts = Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 6 || parts[0] != "GPU" || parts[2] != "BRD" || parts[4] != "TMP")
            {
                base.OnPaint(e);
                return;
            }
            float column = ClientSize.Width / 3f;
            for (int i = 0; i < 3; i++)
            {
                int left = (int)Math.Round(i * column);
                int right = (int)Math.Round((i + 1) * column) - (i == 2 ? 0 : 9);
                Rectangle labelBounds = new Rectangle(left, 0, (int)Math.Round(column * 0.4f), Height);
                Rectangle valueBounds = new Rectangle(labelBounds.Right, 0, Math.Max(1, right - labelBounds.Right), Height);
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                    TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                TextRenderer.DrawText(e.Graphics, parts[i * 2], Font, labelBounds, ForeColor, flags);
                TextRenderer.DrawText(e.Graphics, parts[i * 2 + 1], Font, valueBounds, ForeColor, flags | TextFormatFlags.Right);
            }
        }
    }

    internal static class AlphaWindow
    {
        internal static bool Present(IntPtr window, Bitmap frame, Point location, byte opacity)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                if (screen == IntPtr.Zero) return false;
                memory = CreateCompatibleDC(screen);
                if (memory == IntPtr.Zero) return false;
                BitmapHeader header = new BitmapHeader();
                header.Size = (uint)Marshal.SizeOf(typeof(BitmapHeader));
                header.Width = frame.Width;
                header.Height = -frame.Height; // top-down BGRA, premultiplied alpha
                header.Planes = 1;
                header.BitCount = 32;
                IntPtr pixels;
                bitmap = CreateDIBSection(memory, ref header, 0, out pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) return false;
                BitmapData data = frame.LockBits(new Rectangle(Point.Empty, frame.Size),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int rowBytes = checked(frame.Width * 4);
                    for (int row = 0; row < frame.Height; row++)
                        CopyMemory(IntPtr.Add(pixels, row * rowBytes), IntPtr.Add(data.Scan0, row * data.Stride), (UIntPtr)(uint)rowBytes);
                }
                finally { frame.UnlockBits(data); }
                previous = SelectObject(memory, bitmap);
                Point source = Point.Empty;
                Size size = frame.Size;
                BlendFunction blend = new BlendFunction();
                blend.Alpha = opacity;
                blend.AlphaFormat = 1; // AC_SRC_ALPHA
                return UpdateLayeredWindow(window, screen, ref location, ref size, memory,
                    ref source, 0, ref blend, 2); // ULW_ALPHA
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int XPels, YPels;
            public uint Colors, Important;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction { public byte Operation, Flags, Alpha, AlphaFormat; }
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader info, uint usage,
            out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr bytes);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDC,
            ref Point position, ref Size size, IntPtr memoryDC, ref Point source, uint colorKey,
            ref BlendFunction blend, uint flags);
    }
}
