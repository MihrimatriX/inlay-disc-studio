using ImageMagick;
using ImageMagick.Drawing;

namespace Inlay.Imaging;

/// <summary>The disc's Explorer/AutoPlay icon (§8.2): user icon &gt; rounded cover &gt; default disc.</summary>
public static class DiscIcon
{
    public static readonly uint[] Sizes = [16, 24, 32, 48, 64, 256];

    public static void Write(string outputIco, string? userIcon = null, string? cover = null)
    {
        if (userIcon is not null && Path.GetExtension(userIcon).Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(userIcon, outputIco, overwrite: true);
            return;
        }
        using var master = userIcon is not null ? Square(userIcon, rounded: false)
            : cover is not null ? Square(cover, rounded: true)
            : DefaultDisc();
        var frames = Sizes.Select(size =>
        {
            using var frame = master.Clone();
            frame.Resize(size, size);
            return (size, data: size == 256 ? frame.ToByteArray(MagickFormat.Png) : Dib(frame));
        }).ToList();
        WriteIco(outputIco, frames);
    }

    /// <summary>ICO container: ICONDIR + one ICONDIRENTRY per frame + image data. ImageMagick's own ICO writer stores
    /// the 256 px frame as BMP (~270 KB); Windows expects PNG there, so the container is assembled here.</summary>
    static void WriteIco(string path, IReadOnlyList<(uint size, byte[] data)> frames)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)frames.Count);
        var offset = 6 + 16 * frames.Count;
        foreach (var (size, data) in frames)
        {
            w.Write((byte)(size % 256)); w.Write((byte)(size % 256)); // 0 means 256
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) w.Write(data);
    }

    /// <summary>The DIB (BITMAPINFOHEADER + pixels + mask) ImageMagick writes for a single-frame ICO.</summary>
    static byte[] Dib(IMagickImage<byte> frame)
    {
        var ico = frame.ToByteArray(MagickFormat.Ico);
        var offset = BitConverter.ToInt32(ico, 6 + 12);
        return ico[offset..];
    }

    /// <summary>The default disc picture as a 256 px PNG (for programs whose exe carries no icon).</summary>
    public static void WriteDefaultPng(string path)
    {
        using var disc = DefaultDisc();
        disc.Write(path, MagickFormat.Png);
    }

    static IMagickImage<byte> Square(string path, bool rounded)
    {
        var img = new MagickImage(path);
        img.AutoOrient();
        img.Resize(new MagickGeometry(256, 256) { FillArea = true });
        img.Crop(256, 256, Gravity.Center);
        img.ResetPage();
        if (!rounded) return img;

        using var mask = new MagickImage(MagickColors.Transparent, 256, 256);
        new Drawables().FillColor(MagickColors.White).RoundRectangle(0, 0, 255, 255, 56, 56).Draw(mask);
        img.Alpha(AlphaOption.Set);
        img.Composite(mask, CompositeOperator.DstIn);
        return img;
    }

    /// <summary>Product disc: dark hub over an orange conic ring (design-prompt §5), drawn per pixel with antialiased edges.</summary>
    static IMagickImage<byte> DefaultDisc()
    {
        const int n = 256;
        const double outer = 124, hub = 44, hole = 13;
        (double r, double g, double b)[] stops = [(0xff, 0x8a, 0x3d), (0xff, 0x5a, 0x1f), (0xff, 0xb3, 0x8a), (0xff, 0x8a, 0x3d)];
        var px = new byte[n * n * 4];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            double dx = x + 0.5 - n / 2.0, dy = y + 0.5 - n / 2.0, d = Math.Sqrt(dx * dx + dy * dy);
            var alpha = Math.Clamp(outer - d + 0.5, 0, 1) * Math.Clamp(d - hole + 0.5, 0, 1);
            var t = (Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1 * (stops.Length - 1);
            var (a, b) = (stops[(int)t], stops[(int)t + 1]);
            var f = t - (int)t;
            var ring = (a.r + (b.r - a.r) * f, a.g + (b.g - a.g) * f, a.b + (b.b - a.b) * f);
            var hubMix = Math.Clamp(hub - d + 0.5, 0, 1); // 1 inside the dark hub
            var i = (y * n + x) * 4;
            px[i] = (byte)(ring.Item1 + (0x1b - ring.Item1) * hubMix);
            px[i + 1] = (byte)(ring.Item2 + (0x1c - ring.Item2) * hubMix);
            px[i + 2] = (byte)(ring.Item3 + (0x21 - ring.Item3) * hubMix);
            px[i + 3] = (byte)(alpha * 255);
        }
        return new MagickImage(px, new PixelReadSettings(n, n, StorageType.Char, PixelMapping.RGBA));
    }
}
