param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\assets'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -OutputDirectory $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    return
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class AudioRouteIconBuilder
{
    private static PointF[] Points(params float[] values)
    { var points = new PointF[values.Length / 2]; for (int n = 0; n < points.Length; n++) points[n] = new PointF(values[n*2], values[n*2+1]); return points; }
    private static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var blue = new SolidBrush(Color.FromArgb(25, 90, 166)))
        using (var white = new SolidBrush(Color.White))
        using (var mint = new SolidBrush(Color.FromArgb(98, 224, 209)))
        using (var rounded = new GraphicsPath())
        {
            graphics.Clear(Color.Transparent); graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size / 64F, size / 64F);
            rounded.AddArc(2, 2, 28, 28, 180, 90); rounded.AddArc(34, 2, 28, 28, 270, 90);
            rounded.AddArc(34, 34, 28, 28, 0, 90); rounded.AddArc(2, 34, 28, 28, 90, 90); rounded.CloseFigure();
            graphics.FillPath(blue, rounded);
            graphics.FillPolygon(white, Points(13,26,22,26,35,16,35,48,22,38,13,38));
            graphics.FillPolygon(mint, Points(41,23,51,23,51,18,61,27,51,36,51,31,41,31));
            graphics.FillPolygon(mint, Points(58,39,48,39,48,34,38,43,48,52,48,47,58,47));
        }
        return bitmap;
    }
    private static byte[] Dib(Bitmap bitmap)
    {
        int size = bitmap.Width, maskStride = ((size + 31) / 32) * 4;
        using (var memory = new MemoryStream())
        using (var writer = new BinaryWriter(memory))
        {
            writer.Write(40); writer.Write(size); writer.Write(size * 2);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0);
            writer.Write(size * size * 4 + maskStride * size);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (int y = size - 1; y >= 0; y--) for (int x = 0; x < size; x++)
            { Color c = bitmap.GetPixel(x, y); writer.Write(c.B); writer.Write(c.G); writer.Write(c.R); writer.Write(c.A); }
            for (int y = size - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < size; x++) if (bitmap.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(128 >> (x % 8));
                writer.Write(row);
            }
            return memory.ToArray();
        }
    }
    public static void Generate(string directory)
    {
        int[] sizes = {16, 20, 24, 32, 40, 48, 64, 128, 256};
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) using (var bitmap = Render(sizes[i]))
        { images[i] = Dib(bitmap); if (sizes[i] == 256) bitmap.Save(Path.Combine(directory, "AudioRoute.png"), ImageFormat.Png); }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(directory, "AudioRoute.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
    }
}
'@
[AudioRouteIconBuilder]::Generate([System.IO.Path]::GetFullPath($OutputDirectory))
Write-Output 'Generated AudioRoute.ico and AudioRoute.png (16-256px).'
