[CmdletBinding()]
param([string]$InputPath)
# Converts the supplied artwork to Windows icon sizes; keeps its colors and alpha.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($InputPath)) { $InputPath = Join-Path $PSScriptRoot 'App-source.png' }
$source = (Resolve-Path -LiteralPath $InputPath).Path
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class ScreensaverIconConverter
{
    public static void Build(string source, string folder)
    {
        int[] sizes = {16, 20, 24, 32, 48, 64, 128, 256};
        var images = new byte[sizes.Length][];
        using (var original = Image.FromFile(source))
        for (int i = 0; i < sizes.Length; i++)
        using (var bitmap = new Bitmap(sizes[i], sizes[i], PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = Math.Min(sizes[i] / (float)original.Width, sizes[i] / (float)original.Height);
            float w = original.Width * scale, h = original.Height * scale;
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                var target = Rectangle.Round(new RectangleF((sizes[i]-w)/2, (sizes[i]-h)/2, w, h));
                g.DrawImage(original, target, 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            using (var png = new MemoryStream()) { bitmap.Save(png, ImageFormat.Png); images[i] = png.ToArray(); }
            if (sizes[i] == 256) bitmap.Save(Path.Combine(folder, "App-preview.png"), ImageFormat.Png);
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "App.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (var data in images) writer.Write(data);
        }
    }
}
'@
[ScreensaverIconConverter]::Build($source, $PSScriptRoot)
Write-Output 'Created App.ico (16-256 px) and App-preview.png from the supplied artwork.'
