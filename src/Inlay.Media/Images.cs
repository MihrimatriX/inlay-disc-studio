using System.Text;
using ImageMagick;
using Inlay.Core;

namespace Inlay.Media;

public sealed record PhotoOutput(string File, string Thumb, string? Original, int Width, int Height, string BlurHash, PhotoKind Kind);

/// <summary>What the display copy of a photo will be, known before any pixel is written (for the manifest and capacity).</summary>
public sealed record DisplayPlan(string Extension, int Width, int Height, bool Copy, bool Animated);

/// <summary>Gallery and artwork images (§6.1 image rows): a WebView2-friendly display copy ≤ 2560 px, a 400 px WebP
/// thumbnail and a BlurHash placeholder. HEIC/RAW/TIFF/PSD become AVIF; the original is kept only when asked.</summary>
public static class ImagePipeline
{
    public const uint DisplayMax = 2560, ThumbMax = 400;
    static readonly HashSet<string> Browser = [".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif", ".bmp", ".svg"];
    static readonly HashSet<string> NeedsAvif = [".heic", ".heif", ".tif", ".tiff", ".psd", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2"];

    public static bool IsImage(string path) => Browser.Contains(Ext(path)) || NeedsAvif.Contains(Ext(path));
    public static bool NeedsConversion(string path) => NeedsAvif.Contains(Ext(path));

    public static DisplayPlan PlanDisplay(string source)
    {
        var ext = Ext(source);
        var info = new MagickImageInfo(source);
        var (w, h) = ((int)info.Width, (int)info.Height);
        if (info.Orientation is OrientationType.LeftTop or OrientationType.RightTop or OrientationType.RightBottom or OrientationType.LeftBottom) (w, h) = (h, w);
        var animated = ext is ".gif" or ".webp" && MagickImageInfo.ReadCollection(source).Take(2).Count() > 1;
        if (Browser.Contains(ext) && (animated || ext == ".svg" || Math.Max(w, h) <= DisplayMax))
            return new DisplayPlan(ext, w, h, Copy: true, animated);
        var scale = Math.Min(1.0, (double)DisplayMax / Math.Max(w, h));
        return new DisplayPlan(NeedsAvif.Contains(ext) ? ".avif" : ".webp", (int)Math.Round(w * scale), (int)Math.Round(h * scale), Copy: false, animated);
    }

    /// <summary>Writes the display copy planned by <see cref="PlanDisplay"/> and returns its BlurHash.</summary>
    public static string WriteDisplay(string source, string dest, DisplayPlan plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var image = Load(source);
        if (plan.Copy) File.Copy(source, dest, overwrite: true);
        else
        {
            using var copy = image.Clone();
            Fit(copy, DisplayMax);
            copy.Quality = plan.Extension == ".avif" ? 60u : 85u;
            copy.Strip();
            copy.Write(dest, plan.Extension == ".avif" ? MagickFormat.Avif : MagickFormat.WebP);
        }
        return BlurHashOf(image);
    }

    /// <summary>Artwork and thumbnails as WebP (alpha kept for logos). Artwork limits are widths (§5.3: poster ≤ 780 px wide,
    /// backdrop ≤ 1920 px); thumbnails fit a square box.</summary>
    public static void WriteFitted(string source, string dest, uint max, uint quality = 82, bool widthOnly = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var image = Load(source);
        if (widthOnly) { if (image.Width > max) image.Resize(new MagickGeometry(max, 0)); }
        else Fit(image, max);
        image.Quality = quality;
        image.Strip();
        image.Write(dest, MagickFormat.WebP);
    }

    public static PhotoOutput Prepare(string source, string outDir, string name, bool keepOriginal)
    {
        var plan = PlanDisplay(source);
        var display = Path.Combine(outDir, name + plan.Extension);
        var blur = WriteDisplay(source, display, plan);
        var thumb = Path.Combine(outDir, name + ".thumb.webp");
        WriteFitted(source, thumb, ThumbMax, 80);
        string? original = null;
        if (keepOriginal && NeedsAvif.Contains(Ext(source)))
        {
            original = Path.Combine(outDir, "original", name + Ext(source));
            Directory.CreateDirectory(Path.GetDirectoryName(original)!);
            File.Copy(source, original, overwrite: true);
        }
        return new PhotoOutput(display, thumb, original, plan.Width, plan.Height, blur, plan.Animated ? PhotoKind.Animated : PhotoKind.Image);
    }

    static MagickImage Load(string source)
    {
        var image = new MagickImage(source);
        image.AutoOrient();
        return image;
    }

    static void Fit(IMagickImage<byte> img, uint max)
    {
        if (Math.Max(img.Width, img.Height) > max) img.Resize(new MagickGeometry(max, max) { Greater = true });
    }

    static string BlurHashOf(MagickImage image)
    {
        using var small = image.Clone();
        small.Resize(new MagickGeometry(64, 64));
        var px = small.GetPixels().ToByteArray(PixelMapping.RGB)!;
        return BlurHash.Encode(px, (int)small.Width, (int)small.Height, 4, 3);
    }

    static string Ext(string path) => Path.GetExtension(path).ToLowerInvariant();
}

/// <summary>BlurHash encoder (https://github.com/woltapp/blurhash, algorithm spec), RGB byte input.</summary>
public static class BlurHash
{
    const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

    public static string Encode(byte[] rgb, int width, int height, int cx, int cy)
    {
        var factors = new (double r, double g, double b)[cx * cy];
        for (var j = 0; j < cy; j++)
        for (var i = 0; i < cx; i++)
        {
            double r = 0, g = 0, b = 0, norm = i == 0 && j == 0 ? 1 : 2;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var basis = norm * Math.Cos(Math.PI * i * x / width) * Math.Cos(Math.PI * j * y / height);
                var p = (y * width + x) * 3;
                r += basis * ToLinear(rgb[p]);
                g += basis * ToLinear(rgb[p + 1]);
                b += basis * ToLinear(rgb[p + 2]);
            }
            var scale = 1.0 / (width * height);
            factors[j * cx + i] = (r * scale, g * scale, b * scale);
        }

        var sb = new StringBuilder();
        Base83(sb, cx - 1 + (cy - 1) * 9, 1);
        var maxAc = factors.Skip(1).Select(f => Math.Max(Math.Abs(f.r), Math.Max(Math.Abs(f.g), Math.Abs(f.b)))).DefaultIfEmpty(0).Max();
        var quantMax = factors.Length > 1 ? (int)Math.Clamp(Math.Floor(maxAc * 166 - 0.5), 0, 82) : 0;
        Base83(sb, quantMax, 1);
        var acMax = (quantMax + 1) / 166.0;
        var dc = factors[0];
        Base83(sb, (ToSrgb(dc.r) << 16) + (ToSrgb(dc.g) << 8) + ToSrgb(dc.b), 4);
        foreach (var f in factors.Skip(1))
        {
            int Q(double v) => (int)Math.Clamp(Math.Floor(SignPow(v / acMax, 0.5) * 9 + 9.5), 0, 18);
            Base83(sb, Q(f.r) * 19 * 19 + Q(f.g) * 19 + Q(f.b), 2);
        }
        return sb.ToString();
    }

    static double ToLinear(byte v)
    {
        var c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    static int ToSrgb(double v)
    {
        v = Math.Clamp(v, 0, 1);
        return (int)Math.Round(v <= 0.0031308 ? v * 12.92 * 255 : (1.055 * Math.Pow(v, 1 / 2.4) - 0.055) * 255);
    }

    static double SignPow(double v, double e) => Math.CopySign(Math.Pow(Math.Abs(v), e), v);

    static void Base83(StringBuilder sb, int value, int length)
    {
        for (var i = 1; i <= length; i++)
            sb.Append(Chars[value / (int)Math.Pow(83, length - i) % 83]);
    }
}
