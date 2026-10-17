using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VramMonitorPortable;

internal static class RegressionTests
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        passed++;
    }
    private static bool Exited(int pid)
    {
        try { using (Process p = Process.GetProcessById(pid)) return p.WaitForExit(2000); }
        catch (ArgumentException) { return true; }
    }
    private static FieldInfo Field(string name) { return typeof(VramForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance); }
    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static void TestTypography()
    {
        using (Font font = ModernTypography.Create(16, true, GraphicsUnit.Pixel))
            Check(font.Name == "Segoe UI Semibold", "modern standard font selected without installation");
        Color color = Color.FromArgb(38, 176, 130);
        foreach (int rotation in new[] { 0, 90, 270 })
        {
            using (Bitmap text = ModernTypography.RenderOverlay("123w   67c", 24, color, rotation))
            {
                Check(text.Size == ModernTypography.MeasureOverlay("123w   67c", 24, rotation),
                    "glyph measurement matches rendered size at " + rotation);
                bool transparent = false, smooth = false, solid = false, clean = true;
                for (int y = 0; y < text.Height; y++)
                    for (int x = 0; x < text.Width; x++)
                    {
                        Color pixel = text.GetPixel(x, y);
                        transparent |= pixel.A == 0;
                        smooth |= pixel.A > 0 && pixel.A < 255;
                        solid |= pixel.A == 255;
                        if (pixel.A >= 80 && pixel.A < 230 &&
                            (Math.Abs(pixel.R - color.R) > 8 || Math.Abs(pixel.G - color.G) > 8 || Math.Abs(pixel.B - color.B) > 8))
                        {
                            if (clean) Console.WriteLine("EDGE SAMPLE: " + pixel + "; expected=" + color);
                            clean = false;
                        }
                    }
                Check(transparent && smooth && solid, "transparent antialiased glyph coverage at " + rotation);
                Check(clean, "glyph edges have no dark color-key halo at " + rotation);
            }
        }
        using (OverlayIndicatorForm overlay = new OverlayIndicatorForm())
        {
            overlay.SetColors(color, Color.FromArgb(65, 150, 235));
            overlay.SetGpuTextOptions(true, true, 24, 0, 3, 12, 12);
            overlay.SetGpuMetrics(123, 67);
            overlay.SetUsage(68, 42);
            overlay.Show();
            Application.DoEvents();
            Check(overlay.LastPresentationSucceeded, "per-pixel alpha window presents successfully");
            Check(overlay.TransparencyKey.IsEmpty, "color-key transparency removed");
            foreach (bool left in new[] { false, true })
                foreach (int rotation in new[] { 0, 90, 270 })
                {
                    overlay.SetAnchorLeft(left);
                    overlay.SetGpuTextOptions(true, true, 24, rotation, 3, 12, 12);
                    overlay.SetOpacityPercent(70);
                    Application.DoEvents();
                    Check(overlay.LastPresentationSucceeded, "alpha text rotation/mirror/opacity " + left + "/" + rotation);
                }
            overlay.SetGpuTextOptions(true, true, 24, 0, -2, 12, 12);
            Application.DoEvents();
            Check(overlay.LastPresentationSucceeded, "stacked text remains supported");
            overlay.SetGpuTextOptions(false, false, 24, 0, 3, 12, 12);
            Application.DoEvents();
            Check(overlay.LastPresentationSucceeded, "bars render when text is disabled");
            overlay.SetGpuTextOptions(true, true, 24, 0, 3, 12, 12);
            overlay.SetBehindFullscreen(true);
            overlay.SetOpacityPercent(10);
            Application.DoEvents();
            Check(overlay.LastPresentationSucceeded && !overlay.TopMost, "alpha overlay can move behind fullscreen content");
            overlay.SetBehindFullscreen(false);
            overlay.SetOpacityPercent(100);
            Application.DoEvents();
            Check(overlay.LastPresentationSucceeded && overlay.TopMost, "alpha overlay can return to topmost");
            using (Process process = Process.GetCurrentProcess())
            {
                uint resources = GetGuiResources(process.Handle, 0);
                for (int i = 0; i < 60; i++)
                {
                    overlay.SetGpuMetrics(100 + i, 60 + i % 10);
                    Application.DoEvents();
                }
                Check(GetGuiResources(process.Handle, 0) <= resources + 2, "repeated alpha frames do not leak GDI resources");
            }
            overlay.Close();
        }
        using (Bitmap preview = new Bitmap(760, 320))
        using (Graphics graphics = Graphics.FromImage(preview))
        using (Font caption = ModernTypography.Create(13, false, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.FromArgb(245, 246, 249));
            using (Brush dark = new SolidBrush(Color.FromArgb(25, 29, 36)))
                graphics.FillRectangle(dark, 380, 0, 380, 320);
            graphics.DrawString("Segoe UI Semibold / transparent antialiasing", caption, Brushes.DimGray, 16, 12);
            graphics.DrawString("Same glyphs on a dark background", caption, Brushes.LightGray, 396, 12);
            int y = 52;
            foreach (int pixels in new[] { 16, 24, 36 })
            {
                graphics.DrawString(pixels + "px", caption, Brushes.DimGray, 16, y + 5);
                graphics.DrawString(pixels + "px", caption, Brushes.LightGray, 396, y + 5);
                using (Bitmap text = ModernTypography.RenderOverlay("123w   67c", pixels, color, 0))
                {
                    graphics.DrawImageUnscaled(text, 68, y);
                    graphics.DrawImageUnscaled(text, 448, y);
                }
                y += 58;
            }
            using (Bitmap text = ModernTypography.RenderOverlay("123w   67c", 24, color, 90))
            {
                graphics.DrawImageUnscaled(text, 336, 74);
                graphics.DrawImageUnscaled(text, 716, 74);
            }
            graphics.DrawString("Fixed-column telemetry / modern proportional type", caption, Brushes.DimGray, 16, 248);
            using (TelemetryLabel label = new TelemetryLabel())
            {
                label.Size = new Size(340, 35);
                label.Font = ModernTypography.Create(11, true, GraphicsUnit.Point);
                label.BackColor = Color.FromArgb(245, 246, 249);
                label.ForeColor = Color.FromArgb(30, 35, 44);
                label.Text = "GPU  42%  BRD   123W  TMP  67℃";
                using (Bitmap panel = new Bitmap(340, 35))
                {
                    label.DrawToBitmap(panel, new Rectangle(Point.Empty, panel.Size));
                    graphics.DrawImageUnscaled(panel, 16, 270);
                }
            }
            preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Typography-preview.png"), ImageFormat.Png);
        }
    }


    private static void TestResolutionScaling()
    {
        Check(OverlayIndicatorForm.ScaleForDisplay(new Size(1920, 1080)) == 1d, "Full HD scale is 1x");
        Check(Math.Abs(OverlayIndicatorForm.ScaleForDisplay(new Size(2560, 1440)) - 4d / 3d) < 0.00001,
            "QHD scale is 1.333x");
        Check(OverlayIndicatorForm.ScaleForDisplay(new Size(3840, 2160)) == 2d, "4K scale is 2x");
        Check(OverlayIndicatorForm.ScaleForDisplay(new Size(2160, 3840)) == 2d, "portrait 4K scale is 2x");
        Check(OverlayIndicatorForm.ScaleForDisplay(new Size(3440, 1440)) == 4d / 3d,
            "ultrawide uses short edge, not width");
        Check(OverlayIndicatorForm.ScaleForDisplay(new Size(1280, 720)) == 1d, "small screens do not shrink");
        Check(OverlayIndicatorForm.ScaleForDisplay(new Size(7680, 4320)) == 4d, "8K scale is 4x");
        Check(OverlayIndicatorForm.ScaleForDisplay(new Size(int.MaxValue, int.MaxValue)) == 4d,
            "unexpected huge displays have a bounded scale");

        Color gpu = Color.FromArgb(38, 176, 130), ram = Color.FromArgb(65, 150, 235);
        using (OverlayIndicatorForm overlay = new OverlayIndicatorForm())
        {
            overlay.SetColors(gpu, ram);
            overlay.SetGeometry(3, 150);
            overlay.SetUsage(100, 100);
            foreach (int scale in new[] { 1, 2, 4 })
            {
                Rectangle screen = new Rectangle(-1920, 40, 1920 * scale, 1080 * scale);
                overlay.UpdateDisplayBounds(screen);
                foreach (bool left in new[] { false, true })
                {
                    overlay.SetAnchorLeft(left);
                    using (Bitmap frame = overlay.RenderFrame())
                    {
                        Check(frame.Size == new Size(167 * scale, 167 * scale), "whole overlay extent scales " + scale + "/" + left);
                        Check(overlay.Location == new Point(left ? screen.Left : screen.Right - frame.Width, screen.Top),
                            "corner remains flush at nonzero display origin " + scale + "/" + left);
                        int x = left ? 20 * scale : frame.Width - 20 * scale - 1;
                        Check(frame.GetPixel(x, 3 * scale - 1).ToArgb() == gpu.ToArgb()
                            && frame.GetPixel(x, 3 * scale).A == 0, "bar thickness scales " + scale + "/" + left);
                        int barX = left ? 0 : frame.Width - 1;
                        Check(frame.GetPixel(barX, 150 * scale - 1).ToArgb() == ram.ToArgb()
                            && frame.GetPixel(barX, 150 * scale).A == 0, "bar length scales " + scale + "/" + left);
                        Check(frame.GetPixel(barX, 164 * scale).ToArgb() == ram.ToArgb(),
                            "endpoint gap scales " + scale + "/" + left);
                        Check(frame.GetPixel(frame.Width / 2, frame.Height / 2).A == 0,
                            "overlay interior contains no utilization pointer " + scale + "/" + left);
                    }
                }
            }
            overlay.SetUsage(0, 0);
            overlay.SetGpuMetrics(123, 67);
            foreach (int scale in new[] { 1, 2 })
                foreach (bool left in new[] { false, true })
                    foreach (int rotation in new[] { 0, 90, 270 })
                        foreach (int spacing in new[] { 3, -2 })
                        {
                            overlay.UpdateDisplayBounds(new Rectangle(0, 0, 1920 * scale, 1080 * scale));
                            overlay.SetAnchorLeft(left);
                            overlay.SetGpuTextOptions(true, true, 24, rotation, spacing, 17, 19);
                            using (OverlayMetricLayout layout = new OverlayMetricLayout(24 * scale, true, true, spacing))
                            using (Bitmap expected = layout.Render(123, 67, gpu, rotation))
                            using (Bitmap frame = overlay.RenderFrame())
                            {
                                int x = left ? 17 * scale : frame.Width - 17 * scale - expected.Width;
                                int y = 19 * scale;
                                bool matches = x >= 0 && x + expected.Width <= frame.Width
                                    && y + expected.Height <= frame.Height;
                                for (int dy = 0; matches && dy < expected.Height; dy++)
                                    for (int dx = 0; matches && dx < expected.Width; dx++)
                                        if (expected.GetPixel(dx, dy).A != 0)
                                            matches = frame.GetPixel(x + dx, y + dy).ToArgb() == expected.GetPixel(dx, dy).ToArgb();
                                Check(matches, "native-resolution text and anchored offsets " + scale + "/" + left + "/" + rotation + "/" + spacing);
                            }
                        }
            overlay.SetGpuTextOptions(false, false, 24, 0, 3, 17, 19);
            overlay.UpdateDisplayBounds(new Rectangle(0, 0, 1920, 1080));
            Check(overlay.ClientSize == new Size(167, 167), "return from 4K does not compound scale");
            overlay.UpdateDisplayBounds(Rectangle.Empty);
            Check(overlay.ResolutionScale == 1d && overlay.ClientSize == new Size(167, 167),
                "transient invalid display size is ignored");
            overlay.SetGeometry(3, 3000);
            using (Bitmap frame = overlay.RenderFrame())
                Check(frame.Width <= 1920 && frame.Height <= 1080, "oversized saved geometry fits current screen");
            overlay.UpdateDisplayBounds(new Rectangle(0, 0, 3840, 2160));
            using (Bitmap frame = overlay.RenderFrame())
                Check(frame.Width <= 3840 && frame.Height <= 2160, "oversized saved geometry fits 4K screen");
            overlay.SetGeometry(3, 150);
            overlay.SetGpuTextOptions(true, true, 24, 90, -2, 3000, 3000);
            using (Bitmap frame = overlay.RenderFrame())
            using (OverlayMetricLayout layout = new OverlayMetricLayout(48, true, true, -2))
            using (Bitmap expected = layout.Render(123, 67, gpu, 90))
            {
                Check(frame.Width <= 3840 && frame.Height <= 2160, "large offsets stay inside display");
                Check(frame.GetPixel(0, 0).A == 0, "clamped text offsets do not paint a solid background");
            }
            // Exercise the native alpha presenter at 4K geometry without changing Windows display settings.
            overlay.SetGpuTextOptions(true, true, 24, 0, 3, 17, 19);
            overlay.SetAnchorLeft(true);
            overlay.Show();
            Application.DoEvents();
            Check(overlay.LastPresentationSucceeded, "4K-sized alpha frame presents successfully");
            using (Process process = Process.GetCurrentProcess())
            {
                uint resources = GetGuiResources(process.Handle, 0);
                for (int i = 0; i < 20; i++)
                {
                    int scale = i % 2 + 1;
                    overlay.UpdateDisplayBounds(new Rectangle(0, 0, 1920 * scale, 1080 * scale));
                    Application.DoEvents();
                }
                Check(GetGuiResources(process.Handle, 0) <= resources + 2, "display changes do not leak GDI handles");
            }
            overlay.Hide();

            using (Bitmap preview = new Bitmap(900, 430))
            using (Graphics graphics = Graphics.FromImage(preview))
            using (Font caption = ModernTypography.Create(14, false, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.FromArgb(25, 29, 36));
                overlay.SetUsage(68, 42);
                overlay.SetAnchorLeft(false);
                foreach (int scale in new[] { 1, 2 })
                {
                    overlay.UpdateDisplayBounds(new Rectangle(0, 0, 1920 * scale, 1080 * scale));
                    overlay.SetGpuTextOptions(true, true, 19, 0, 1, 17, 12);
                    graphics.DrawString(scale == 1 ? "1920 x 1080 : 1x" : "3840 x 2160 : 2x",
                        caption, Brushes.LightGray, scale == 1 ? 20 : 350, 18);
                    using (Bitmap frame = overlay.RenderFrame())
                        graphics.DrawImageUnscaled(frame, (scale == 1 ? 300 : 860) - frame.Width, 60);
                }
                preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resolution-preview.png"), ImageFormat.Png);
            }
        }
    }


    private static bool EqualPixels(Bitmap first, Bitmap second, Rectangle area, int coverageTolerance = 0)
    {
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (coverageTolerance > 0)
                {
                    if (Math.Abs(first.GetPixel(x, y).A - second.GetPixel(x, y).A) > coverageTolerance) return false;
                }
                else if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb()) return false;
        return true;
    }

    private static void Unrotate(Bitmap bitmap, int rotation)
    {
        if (rotation == 90) bitmap.RotateFlip(RotateFlipType.Rotate270FlipNone);
        else if (rotation == 270) bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
    }

    private static void TestStableMetricLayout()
    {
        Color color = Color.FromArgb(38, 176, 130);
        foreach (int pixels in new[] { 19, 38 })
            foreach (int spacing in new[] { 0, 3, -1, -2 })
                foreach (int rotation in new[] { 0, 90, 270 })
                {
                    using (OverlayMetricLayout layout = new OverlayMetricLayout(pixels, true, true, spacing))
                    using (OverlayMetricLayout powerOnly = new OverlayMetricLayout(pixels, true, false, spacing))
                    using (GraphicsPath w = ModernTypography.TextPath("w", pixels))
                    using (GraphicsPath c = ModernTypography.TextPath("c", pixels))
                    using (Bitmap baseline = layout.Render(888, 88, color, rotation))
                    {
                        Size rotatedSize = baseline.Size;
                        Unrotate(baseline, rotation);
                        int singleHeight = powerOnly.SizeAtRotation(0).Height;
                        int powerWidth = spacing >= 0 ? powerOnly.SizeAtRotation(0).Width : baseline.Width;
                        int wWidth = (int)Math.Ceiling(w.GetBounds().Width);
                        int cWidth = (int)Math.Ceiling(c.GetBounds().Width);
                        Rectangle powerUnit = new Rectangle(powerWidth - 1 - wWidth, 0, wWidth, singleHeight);
                        Rectangle tempUnit = new Rectangle(baseline.Width - 1 - cWidth,
                            spacing < 0 ? baseline.Height - singleHeight : 0, cWidth, singleHeight);
                        bool stable = true, clean = true;
                        foreach (int value in new[] { -1, 0, 1, 8, 9, 11, 99, 100, 111, 888, 999, 1000, 9999, int.MaxValue })
                            using (Bitmap current = layout.Render(value, value, color, rotation))
                            {
                                stable &= current.Size == rotatedSize && current.Size == layout.SizeAtRotation(rotation);
                                Unrotate(current, rotation);
                                // At zero gap, neighboring glyphs may affect bicubic coverage by 1/255.
                                bool powerFixed = EqualPixels(baseline, current, powerUnit, 2);
                                bool temperatureFixed = EqualPixels(baseline, current, tempUnit, 2);
                                stable &= powerFixed && temperatureFixed;
                                for (int y = 0; clean && y < current.Height; y++)
                                    for (int x = 0; clean && x < current.Width; x++)
                                    {
                                        Color pixel = current.GetPixel(x, y);
                                        if (pixel.A >= 80 && pixel.A < 230)
                                            clean &= Math.Abs(pixel.R - color.R) <= 8
                                                && Math.Abs(pixel.G - color.G) <= 8 && Math.Abs(pixel.B - color.B) <= 8;
                                    }
                            }
                        Check(stable, "metric size, baseline and units stay fixed " + pixels + "/" + spacing + "/" + rotation);
                        Check(clean, "tabular text retains clean antialiased edges " + pixels + "/" + spacing + "/" + rotation);
                    }
                }
        foreach (bool power in new[] { false, true })
            using (OverlayMetricLayout layout = new OverlayMetricLayout(24, power, !power, 1))
            using (Bitmap a = layout.Render(1, 1, color, 0))
            using (Bitmap b = layout.Render(8888, 888, color, 0))
                Check(a.Size == b.Size, "single metric remains fixed width " + power);

        using (OverlayMetricLayout layout = new OverlayMetricLayout(24, true, true, 3))
        using (OverlayMetricLayout powerOnly = new OverlayMetricLayout(24, true, false, 3))
        using (Bitmap first = layout.Render(9, 67, color, 0))
        using (Bitmap second = layout.Render(1000, 67, color, 0))
        {
            int x = powerOnly.SizeAtRotation(0).Width;
            Check(EqualPixels(first, second, new Rectangle(x, 0, first.Width - x, first.Height)),
                "changing power digit count does not move temperature");
        }
        using (OverlayMetricLayout layout = new OverlayMetricLayout(24, true, true, 3))
        using (OverlayMetricLayout powerOnly = new OverlayMetricLayout(24, true, false, 3))
        using (Bitmap first = layout.Render(123, 9, color, 0))
        using (Bitmap second = layout.Render(123, 100, color, 0))
            Check(EqualPixels(first, second, new Rectangle(0, 0, powerOnly.SizeAtRotation(0).Width, first.Height)),
                "changing temperature digit count does not move power");

        using (OverlayMetricLayout layout = new OverlayMetricLayout(24, true, false, 3))
        using (Bitmap first = layout.Render(111, 0, color, 0))
        using (Bitmap second = layout.Render(811, 0, color, 0))
        {
            int advance = (int)typeof(OverlayMetricLayout).GetField("digitAdvance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layout);
            Check(EqualPixels(first, second, new Rectangle(1 + 2 * advance, 0, first.Width - 1 - 2 * advance, first.Height)),
                "changing hundreds glyph does not move tens or ones");
        }
        using (OverlayIndicatorForm overlay = new OverlayIndicatorForm())
        {
            overlay.UpdateDisplayBounds(new Rectangle(0, 0, 3840, 2160));
            overlay.SetColors(color, Color.Blue);
            overlay.SetGpuTextOptions(true, true, 24, 0, 3, 17, 19);
            foreach (bool left in new[] { false, true })
                foreach (int rotation in new[] { 0, 90, 270 })
                {
                    overlay.SetAnchorLeft(left);
                    overlay.SetGpuTextOptions(true, true, 24, rotation, 3, 17, 19);
                    overlay.SetGpuMetrics(9, 9);
                    Size size = overlay.Size;
                    Point position = overlay.Location;
                    using (Bitmap first = overlay.RenderFrame())
                    {
                        overlay.SetGpuMetrics(1000, 100);
                        using (Bitmap second = overlay.RenderFrame())
                            Check(overlay.Size == size && overlay.Location == position && first.Size == second.Size,
                                "actual overlay frame stays fixed across digit counts " + left + "/" + rotation);
                    }
                }
            overlay.SetGpuTextOptions(true, true, 19, 0, 3, 17, 19);
            overlay.Show();
            Application.DoEvents();
            using (Process process = Process.GetCurrentProcess())
            {
                uint handles = GetGuiResources(process.Handle, 0);
                for (int i = 0; i < 30; i++)
                {
                    overlay.SetGpuTextOptions(true, true, i % 2 == 0 ? 19 : 24, 90 * (i % 2), i % 2 == 0 ? -1 : 3, 17, 19);
                    overlay.SetGpuMetrics(i * 37, i % 100);
                    Application.DoEvents();
                }
                Check(overlay.LastPresentationSucceeded && GetGuiResources(process.Handle, 0) <= handles + 2,
                    "tabular layout changes release glyphs and preserve native rendering");
            }
            overlay.Close();
        }
        using (Bitmap preview = new Bitmap(880, 400))
        using (Graphics graphics = Graphics.FromImage(preview))
        using (Font caption = ModernTypography.Create(14, false, GraphicsUnit.Pixel))
        using (OverlayMetricLayout layout = new OverlayMetricLayout(32, true, true, 3))
        using (OverlayMetricLayout stacked = new OverlayMetricLayout(32, true, true, -1))
        {
            graphics.Clear(Color.FromArgb(25, 29, 36));
            graphics.DrawString("Fixed digit cells / fixed units and baseline", caption, Brushes.LightGray, 24, 15);
            int[] power = { 1, 11, 111, 888, 1000, -1 };
            int[] temperature = { 1, 11, 111, 88, 99, -1 };
            for (int i = 0; i < power.Length; i++)
                using (Bitmap text = layout.Render(power[i], temperature[i], color, 0))
                    graphics.DrawImageUnscaled(text, 30, 55 + i * 48);
            graphics.DrawString("Stacked", caption, Brushes.LightGray, 360, 15);
            using (Bitmap text = stacked.Render(123, 67, color, 0))
                graphics.DrawImageUnscaled(text, 360, 55);
            graphics.DrawString("90 / 270 degrees", caption, Brushes.LightGray, 570, 15);
            using (Bitmap text = layout.Render(123, 67, color, 90))
                graphics.DrawImageUnscaled(text, 600, 55);
            using (Bitmap text = layout.Render(123, 67, color, 270))
                graphics.DrawImageUnscaled(text, 720, 55);
            preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tabular-preview.png"), ImageFormat.Png);
        }
    }

    private static void TestNvidiaIdentity()
    {
        const string name = "NVIDIA GeForce RTX 5070 Ti";
        GpuPciAddress a = new GpuPciAddress(1, 0, 0);
        GpuPciAddress b = new GpuPciAddress(2, 0, 0);
        GpuTelemetryProvider.NvidiaCandidate first = new GpuTelemetryProvider.NvidiaCandidate(new IntPtr(1), name, a);
        GpuTelemetryProvider.NvidiaCandidate second = new GpuTelemetryProvider.NvidiaCandidate(new IntPtr(2), name, b);
        string reason;
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, a, false, new[] { first }, true, out reason) == 0,
            "duplicate logical names do not block matching physical PCI GPU");
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, b, false, new[] { first, second }, true, out reason) == 1,
            "two identical models select correct PCI GPU");
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, a, false, new[] { first, second }, true, out reason) == 0,
            "two identical models select first PCI GPU when requested");
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, b, true, new[] { first }, true, out reason) == -1,
            "known PCI mismatch never falls back to another card");
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, null, false, new[] { first }, true, out reason) == 0,
            "single physical NVIDIA GPU tolerates duplicate DXGI names without PCI query");
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, null, false, new[] { first, second }, true, out reason) == -1,
            "two physical same-name GPUs without identity remain unavailable");
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, a, false, new[] { first }, false, out reason) == -1,
            "incomplete physical GPU enumeration remains unavailable");
        Check(GpuTelemetryProvider.SelectNvidiaCandidate("NVIDIA GeForce RTX 5080", null, false, new[] { first }, true, out reason) == -1,
            "single GPU with a different model is not selected");
        GpuTelemetryProvider.NvidiaCandidate unknown = new GpuTelemetryProvider.NvidiaCandidate(new IntPtr(2), name, null);
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, a, false, new[] { first, unknown }, true, out reason) == -1,
            "partially missing PCI identities do not cause ambiguous selection");
        GpuTelemetryProvider.NvidiaCandidate sameBus = new GpuTelemetryProvider.NvidiaCandidate(new IntPtr(2), name, a);
        Check(GpuTelemetryProvider.SelectNvidiaCandidate(name, a, false, new[] { first, sameBus }, true, out reason) == -1,
            "duplicate bus addresses across domains stay ambiguous");
        Check(GpuPciAddress.ParseNvml("00000000:01:00.0").Matches(a), "NVML extended PCI address parsing");
        Check(GpuPciAddress.ParseNvml("0000:02:00.0").Matches(b), "NVML legacy PCI address parsing");
        Check(GpuPciAddress.ParseNvml("0000:01:00.1").Function == 1, "PCI function parsed rather than assumed zero");
        Check(GpuPciAddress.ParseNvml("invalid") == null, "malformed PCI address rejected");
        Check(GpuPciAddress.ParseNvml("0000:01:20.0") == null, "out-of-range PCI device rejected");
        Check(Marshal.SizeOf(typeof(GpuTelemetryProvider.NvmlPciInfo)) == 68, "NVML PCI structure ABI size");
        Check(Marshal.OffsetOf(typeof(GpuTelemetryProvider.NvmlPciInfo), "BusId").ToInt32() == 36, "NVML PCI bus-id ABI offset");
        Check(Marshal.SizeOf(typeof(GpuPciProbe.OpenAdapter)) == 12, "Windows LUID adapter structure ABI size");
        Check(Marshal.SizeOf(typeof(GpuPciProbe.QueryAdapter)) == 24, "Windows adapter-query ABI size");
        Check(Marshal.SizeOf(typeof(GpuPciProbe.AdapterAddress)) == 12, "Windows PCI-address ABI size");
        VramAdapterInfo[] adapters;
        string error;
        Check(VramCapacityProbe.TryGetAdapters(out adapters, out error), "live DXGI adapters enumerated for PCI test");
        bool found = false;
        foreach (VramAdapterInfo adapter in adapters)
        {
            GpuPciAddress pci = GpuPciProbe.Read(adapter.LuidLow, adapter.LuidHigh, out error);
            Console.WriteLine("LIVE GPU IDENTITY: " + adapter.Name + ": " + error);
            found |= pci != null;
        }
        Check(found, "live Windows LUID to physical PCI mapping");
    }

    private static void TestNvidiaSensors()
    {
        GpuTelemetryProvider.UIntSensor util = delegate(out uint value) { value = 42; return 0; };
        GpuTelemetryProvider.UIntSensor power = delegate(out uint value) { value = 123400; return 0; };
        GpuTelemetryProvider.UIntSensor temp = delegate(out uint value) { value = 67; return 0; };
        GpuTelemetryProvider.UIntSensor unsupported = delegate(out uint value) { value = 0; return 3; };
        GpuTelemetryProvider.UIntSensor missing = delegate(out uint value) { throw new EntryPointNotFoundException(); };
        int fallbackCalls = 0;
        GpuTelemetryProvider.UIntSensor unused = delegate(out uint value) { fallbackCalls++; value = 0; return 3; };
        GpuTelemetryReading reading = GpuTelemetryProvider.ReadNvidiaSensors(util, power, temp, unused, unused);
        Check(reading.HasUtilization && reading.HasBoardPower && reading.HasTemperature, "NVIDIA existing sensor APIs still work");
        Check(fallbackCalls == 0, "NVIDIA fallbacks skipped when existing APIs succeed");
        Check(Math.Abs(reading.BoardPowerWatts - 123.4) < 0.001, "NVIDIA milliwatts converted to watts");
        reading = GpuTelemetryProvider.ReadNvidiaSensors(util, unsupported, unsupported, power, temp);
        Check(reading.HasBoardPower && reading.BoardPowerWatts == 123.4, "NVIDIA alternative power restores missing power");
        Check(reading.HasTemperature && reading.TemperatureCelsius == 67, "NVIDIA versioned temperature restores missing temperature");
        Check(reading.Diagnostic.Contains("power=3") && reading.Diagnostic.Contains("temperature-v1=0"), "NVIDIA API result diagnostics retained");
        reading = GpuTelemetryProvider.ReadNvidiaSensors(util, unsupported, unsupported, missing, missing);
        Check(reading.HasUtilization && reading.UtilizationPercent == 42, "NVIDIA partial sensor failures preserve utilization");
        Check(!reading.HasBoardPower && !reading.HasTemperature, "NVIDIA unavailable APIs never fabricate readings");
        reading = GpuTelemetryProvider.ReadNvidiaSensors(util, power, missing, unused, temp);
        Check(reading.HasBoardPower && reading.HasTemperature, "missing temperature entry point does not discard power");
        GpuTelemetryProvider.UIntSensor invalidTemp = delegate(out uint value) { value = 999; return 0; };
        reading = GpuTelemetryProvider.ReadNvidiaSensors(util, power, invalidTemp, unused, temp);
        Check(reading.TemperatureCelsius == 67, "invalid NVIDIA temperature uses fallback");
        GpuTelemetryProvider.UIntSensor zero = delegate(out uint value) { value = 0; return 0; };
        reading = GpuTelemetryProvider.ReadNvidiaSensors(zero, zero, temp, unused, unused);
        Check(reading.HasBoardPower && reading.BoardPowerWatts == 0, "zero power remains a valid measurement");
        Check(Marshal.SizeOf(typeof(GpuTelemetryProvider.NvmlFieldValue)) == 40, "NVML field ABI size");
        Check(Marshal.OffsetOf(typeof(GpuTelemetryProvider.NvmlFieldValue), "Value").ToInt32() == 32, "NVML field union ABI offset");
        Check(Marshal.SizeOf(typeof(GpuTelemetryProvider.NvmlTemperature)) == 12, "NVML versioned temperature ABI size");
        GpuTelemetryProvider.NvmlFieldValue field = new GpuTelemetryProvider.NvmlFieldValue();
        field.FieldId = 185;
        field.ValueType = 1;
        field.Value.UInt32 = 123400;
        uint result;
        Check(GpuTelemetryProvider.TryGetPowerField(field, out result) && result == 123400, "NVIDIA average power field");
        field.FieldId = 186;
        field.ValueType = 3;
        field.Value.UInt64 = 124000;
        Check(GpuTelemetryProvider.TryGetPowerField(field, out result) && result == 124000, "NVIDIA instantaneous power field");
        field.ReturnCode = 3;
        Check(!GpuTelemetryProvider.TryGetPowerField(field, out result), "NVIDIA field-level error rejected");
        field.ReturnCode = 0;
        field.FieldId = 190;
        Check(!GpuTelemetryProvider.TryGetPowerField(field, out result), "power limit cannot masquerade as consumption");
        field.FieldId = 185;
        field.ScopeId = 1;
        Check(!GpuTelemetryProvider.TryGetPowerField(field, out result), "module power cannot masquerade as GPU power");
        field.ScopeId = 0;
        field.ValueType = 0;
        field.Value.Double = double.NaN;
        Check(!GpuTelemetryProvider.TryGetPowerField(field, out result), "invalid field value rejected");
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--monitor-worker")
        {
            MonitorWorker.Run(args[1], args[2]);
            return 0;
        }
        if (args.Length == 1 && args[0] == "--parent-probe")
        {
            using (MonitorWorkerClient worker = new MonitorWorkerClient("vram"))
            {
                Console.WriteLine(worker.Request("PID"));
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
            }
            return 0;
        }
        if (args.Length == 1 && args[0] == "--benchmark")
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (VramForm form = new VramForm())
            {
                Process[] processes = null;
                double cpu = 0;
                int handles = 0;
                Stopwatch sample = new Stopwatch();
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
                {
                    timer.Interval = 5000;
                    timer.Tick += delegate
                    {
                        if (processes == null)
                        {
                            processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Application.ExecutablePath));
                            foreach (Process process in processes) { cpu += process.TotalProcessorTime.TotalSeconds; handles += process.HandleCount; }
                            sample.Start();
                            timer.Interval = 20000;
                            return;
                        }
                        timer.Stop();
                        double cpuEnd = 0, memory = 0;
                        int handlesEnd = 0;
                        foreach (Process process in processes)
                        {
                            process.Refresh();
                            if (process.HasExited) throw new Exception("Worker restarted during benchmark");
                            cpuEnd += process.TotalProcessorTime.TotalSeconds;
                            memory += process.PrivateMemorySize64 / 1048576d;
                            handlesEnd += process.HandleCount;
                            Console.WriteLine("STABLE PID: " + process.Id);
                            process.Dispose();
                        }
                        Console.WriteLine("BENCHMARK: processes={0}, seconds={1:F2}, oneCoreCpuPercent={2:F3}, totalPrivateMiB={3:F2}, handles={4}->{5}",
                            processes.Length, sample.Elapsed.TotalSeconds, (cpuEnd - cpu) / sample.Elapsed.TotalSeconds * 100,
                            memory, handles, handlesEnd);
                        Console.WriteLine("LIVE VRAM: " + ((Label)Field("valueLabel").GetValue(form)).Text);
                        Console.WriteLine("LIVE GPU: " + ((Label)Field("telemetryLabel").GetValue(form)).Text);
                        form.ExitApplication();
                    };
                    form.Shown += delegate { timer.Start(); };
                    Application.Run(form);
                }
            }
            return 0;
        }
        try
        {
            TestNvidiaSensors();
            TestNvidiaIdentity();
            string config = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.ini");
            // Run only in the dedicated test output directory, never beside user settings.
            if (File.Exists(config)) throw new Exception("Test directory already contains Config.ini; use a fresh test directory.");
            OverlayConfiguration settings = OverlayConfiguration.Load();
            Check(!OverlayConfiguration.SaveBlocked, "missing config permits first save");
            settings.Length = 321;
            settings.Save();
            settings.Length = 456;
            settings.Save();
            Check(File.ReadAllText(config).Contains("Length=456"), "atomic settings save");
            Check(File.ReadAllText(config + ".bak").Contains("Length=321"), "previous settings backup");
            Check(OverlayConfiguration.Load().Length == 456, "settings round trip");
            File.AppendAllText(config, "\r\nGpuNeedleEnabled=True\r\nGpuNeedleLength=200\r\nGpuNeedleThickness=4\r\nGpuNeedleTipRadius=6\r\n");
            settings = OverlayConfiguration.Load();
            Check(settings.Length == 456, "legacy pointer keys do not affect remaining settings");
            settings.Save();
            Check(!File.ReadAllText(config).Contains("GpuNeedle"), "obsolete pointer settings are no longer saved");
            using (FileStream locked = new FileStream(config, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                OverlayConfiguration.Load();
                Check(OverlayConfiguration.SaveBlocked, "read failure blocks overwrite");
                bool refused = false;
                try { settings.Save(); } catch (IOException) { refused = true; }
                Check(refused, "unreadable settings are never replaced");
            }
            Check(OverlayConfiguration.Load().Length == 456, "settings survive read failure");
            VramAdapterInfo first = new VramAdapterInfo("NVIDIA GeForce RTX 5070 Ti", 16000, 1, 0);
            VramAdapterInfo second = new VramAdapterInfo(first.Name, 16000, 2, 0);
            Check(!MonitorWorker.HasUniqueName(new[] { first, second }, first), "identical GPUs rejected instead of misidentified");
            Check(MonitorWorker.HasUniqueName(new[] { first }, first), "unique GPU accepted");
            bool invalidRejected = false;
            try { VramCounter.RequireValidUsage(-1); } catch (IOException) { invalidRejected = true; }
            Check(invalidRejected, "missing VRAM counters are not reported as zero");
            Check(VramCounter.RequireValidUsage(0) == 0, "valid zero VRAM is accepted");
            MethodInfo registration = typeof(VramForm).GetMethod("IsApplicationStartupCommand", BindingFlags.NonPublic | BindingFlags.Static);
            Check((bool)registration.Invoke(null, new object[] { "\"C:\\old folder\\SYSTEM Monitor Overlay.exe\" --startup" }), "old path startup recognized");
            Check((bool)registration.Invoke(null, new object[] { "\"C:\\old\\VramMonitor-Portable.exe\" --startup" }), "legacy startup recognized");
            Check(!(bool)registration.Invoke(null, new object[] { "\"C:\\other\\Other.exe\" --startup" }), "unrelated startup rejected");

            int child;
            using (MonitorWorkerClient worker = new MonitorWorkerClient("vram"))
            {
                child = int.Parse(worker.Request("PID"));
                for (int i = 0; i < 12; i++)
                    Check(int.Parse(worker.Request("PID")) == child, "worker reused " + i);
                Check(worker.Request("ERROR").StartsWith("ERROR|"), "measurement failure is reported");
                Check(int.Parse(worker.Request("PID")) == child, "worker recovers after measurement failure");
                Stopwatch clock = Stopwatch.StartNew();
                bool timeout = false;
                try { worker.Request("HANG"); } catch (TimeoutException) { timeout = true; }
                Check(timeout && clock.ElapsedMilliseconds < 5000, "hung measurement has bounded timeout");
                Check(Exited(child), "hung child terminated");
                int restarted = int.Parse(worker.Request("PID"));
                Check(restarted != child, "next request restarts failed worker");
                child = restarted;
            }
            Check(Exited(child), "dispose terminates child");

            for (int i = 0; i < 5; i++)
            {
                MonitorWorkerClient worker = new MonitorWorkerClient("vram");
                Task request = Task.Run(delegate { try { worker.Request("HANG"); } catch { } });
                Thread.Sleep(10);
                worker.Dispose();
                Check(request.Wait(5000), "concurrent startup and exit " + i);
            }
            ProcessStartInfo probe = new ProcessStartInfo(Application.ExecutablePath, "--parent-probe");
            probe.UseShellExecute = false;
            probe.CreateNoWindow = true;
            probe.RedirectStandardOutput = true;
            using (Process parent = Process.Start(probe))
            {
                Task<string> line = parent.StandardOutput.ReadLineAsync();
                Check(line.Wait(5000), "parent probe started");
                child = int.Parse(line.Result);
                parent.Kill();
                Check(parent.WaitForExit(3000), "test parent terminated");
                Check(Exited(child), "job terminates child after abrupt parent exit");
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            byte[] original = File.ReadAllBytes(config);
            TestTypography();
            TestResolutionScaling();
            TestStableMetricLayout();
            using (VramForm form = new VramForm())
            {
                IntPtr handle = form.Handle;
                bool noPointerControls = true;
                foreach (Control control in form.Controls)
                    if (control.Text.Contains("針")) noPointerControls = false;
                Check(noPointerControls, "removed pointer controls are absent from settings");
                Check(Field("gpuNeedleToggle") == null && Field("gpuNeedleLengthInput") == null,
                    "removed pointer controls are absent from implementation");
                Field("activeGpuKey").SetValue(form, "1|2");
                MethodInfo post = typeof(VramForm).GetMethod("PostTelemetry", BindingFlags.Instance | BindingFlags.NonPublic);
                post.Invoke(form, new object[] { "STALE", 99, 999, 99, "3|4" });
                Application.DoEvents();
                Check(((Label)Field("telemetryLabel").GetValue(form)).Text != "STALE", "old GPU result rejected");
                post.Invoke(form, new object[] { "CURRENT", 42, 123, 67, "1|2" });
                Application.DoEvents();
                Check(((Label)Field("telemetryLabel").GetValue(form)).Text == "CURRENT", "current GPU result accepted");
                form.Show();
                Application.DoEvents();
                Check(!form.Visible, "starts hidden in tray");
                form.RestoreFromTray();
                Application.DoEvents();
                Check(form.Visible, "tray restore shows window");
                form.Close();
                Application.DoEvents();
                Check(!form.Visible && !form.IsDisposed, "X hides without exiting");
                Stopwatch live = Stopwatch.StartNew();
                while (live.ElapsedMilliseconds < 6500) { Application.DoEvents(); Thread.Sleep(20); }
                Console.WriteLine("LIVE VRAM: " + ((Label)Field("valueLabel").GetValue(form)).Text);
                Console.WriteLine("LIVE GPU: " + ((Label)Field("telemetryLabel").GetValue(form)).Text);
                Check(((Label)Field("ramLabel").GetValue(form)).Text.Contains("GiB"), "RAM remains available independently");
                form.RestoreFromTray();
                Application.DoEvents();
                using (Bitmap preview = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                    preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings-preview.png"), ImageFormat.Png);
                }
                form.ExitApplication();
                Application.DoEvents();
                Check(form.IsDisposed, "normal Exit disposes form");
            }
            Check(Convert.ToBase64String(original) == Convert.ToBase64String(File.ReadAllBytes(config)),
                "opening and closing does not rewrite config");
            Environment.SetEnvironmentVariable("SMO_TEST_INVALID_VRAM", "1");
            try
            {
                using (MonitorWorkerClient worker = new MonitorWorkerClient("vram"))
                    Check(worker.Request("READ").StartsWith("ERROR|No valid VRAM counters"), "invalid counter status crosses process boundary as error");
            }
            finally { Environment.SetEnvironmentVariable("SMO_TEST_INVALID_VRAM", null); }
            using (MonitorWorkerClient worker = new MonitorWorkerClient("vram"))
                Check(worker.Request("READ").StartsWith("OK|"), "VRAM reads recover after simulated failure");
            Console.WriteLine("TOTAL PASSED: " + passed);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
