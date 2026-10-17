param(
    [Parameter(Mandatory = $true)][string]$SourceImage,
    [Parameter(Mandatory = $true)][string]$OutputIcon,
    [string]$TransparentPng
)

Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

public static class TransparentIconProcessor
{
    public static Bitmap RemoveConnectedWhite(Image source)
    {
        Bitmap image = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(image))
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);

        int width = image.Width;
        int height = image.Height;
        bool[] outside = new bool[width * height];
        Queue<Point> queue = new Queue<Point>();

        Action<int, int> enqueue = delegate(int x, int y)
        {
            int offset = y * width + x;
            if (outside[offset]) return;
            Color color = image.GetPixel(x, y);
            int minimum = Math.Min(color.R, Math.Min(color.G, color.B));
            int maximum = Math.Max(color.R, Math.Max(color.G, color.B));
            if (minimum < 90 || maximum - minimum > 35) return;
            outside[offset] = true;
            queue.Enqueue(new Point(x, y));
        };

        for (int x = 0; x < width; x++)
        {
            enqueue(x, 0);
            enqueue(x, height - 1);
        }
        for (int y = 0; y < height; y++)
        {
            enqueue(0, y);
            enqueue(width - 1, y);
        }

        while (queue.Count > 0)
        {
            Point point = queue.Dequeue();
            if (point.X > 0) enqueue(point.X - 1, point.Y);
            if (point.X + 1 < width) enqueue(point.X + 1, point.Y);
            if (point.Y > 0) enqueue(point.X, point.Y - 1);
            if (point.Y + 1 < height) enqueue(point.X, point.Y + 1);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!outside[y * width + x]) continue;
                Color color = image.GetPixel(x, y);
                int minimum = Math.Min(color.R, Math.Min(color.G, color.B));
                int alpha = 255 - minimum;
                if (alpha <= 7)
                {
                    image.SetPixel(x, y, Color.Transparent);
                    continue;
                }

                int red = Recover(color.R, alpha);
                int green = Recover(color.G, alpha);
                int blue = Recover(color.B, alpha);
                image.SetPixel(x, y, Color.FromArgb(alpha, red, green, blue));
            }
        }
        return image;
    }

    private static int Recover(int component, int alpha)
    {
        int value = (component * 255 - 255 * (255 - alpha)) / alpha;
        return Math.Max(0, Math.Min(255, value));
    }

    public static byte[] EncodeIconDib(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int maskStride = ((width + 31) / 32) * 4;
        using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
        using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
        {
            writer.Write(40);               // BITMAPINFOHEADER size
            writer.Write(width);
            writer.Write(height * 2);       // XOR image plus AND mask
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0);
            writer.Write(width * height * 4);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    writer.Write(color.B);
                    writer.Write(color.G);
                    writer.Write(color.R);
                    writer.Write(color.A);
                }
            }

            byte[] maskRow = new byte[maskStride];
            for (int y = height - 1; y >= 0; y--)
            {
                Array.Clear(maskRow, 0, maskRow.Length);
                for (int x = 0; x < width; x++)
                {
                    if (bitmap.GetPixel(x, y).A < 8)
                        maskRow[x / 8] |= (byte)(0x80 >> (x % 8));
                }
                writer.Write(maskRow);
            }
            writer.Flush();
            return stream.ToArray();
        }
    }
}
'@

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$original = [System.Drawing.Image]::FromFile($SourceImage)
$source = [TransparentIconProcessor]::RemoveConnectedWhite($original)
$original.Dispose()
if ($TransparentPng) {
    $source.Save($TransparentPng, [System.Drawing.Imaging.ImageFormat]::Png)
}
$images = New-Object System.Collections.Generic.List[byte[]]

try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)
            }
            finally {
                $graphics.Dispose()
            }

            $images.Add([TransparentIconProcessor]::EncodeIconDib($bitmap))
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$file = [System.IO.File]::Create($OutputIcon)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([Byte]$sizeByte)
        $writer.Write([Byte]$sizeByte)
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$images[$index].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$index].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}
