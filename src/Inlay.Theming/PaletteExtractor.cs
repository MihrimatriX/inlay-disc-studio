using ImageMagick;
using Inlay.Core;

namespace Inlay.Theming;

/// <summary>Perceptual color in OKLab (L 0..1, a/b around 0).</summary>
public readonly record struct Oklab(double L, double A, double B)
{
    public double Chroma => Math.Sqrt(A * A + B * B);

    public static Oklab FromSrgb(byte r, byte g, byte b)
    {
        double lr = Lin(r), lg = Lin(g), lb = Lin(b);
        var l = Math.Cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
        var m = Math.Cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
        var s = Math.Cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);
        return new(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                   1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                   0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    public (byte R, byte G, byte B) ToSrgb()
    {
        var l = Math.Pow(L + 0.3963377774 * A + 0.2158037573 * B, 3);
        var m = Math.Pow(L - 0.1055613458 * A - 0.0638541728 * B, 3);
        var s = Math.Pow(L - 0.0894841775 * A - 1.2914855480 * B, 3);
        return (Gam(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
                Gam(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
                Gam(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    public string Hex
    {
        get { var (r, g, b) = ToSrgb(); return $"#{r:x2}{g:x2}{b:x2}"; }
    }

    public Oklab WithL(double l) => this with { L = Math.Clamp(l, 0, 1) };
    public Oklab WithChroma(double c) => Chroma < 1e-9 ? this : this with { A = A / Chroma * c, B = B / Chroma * c };

    static double Lin(byte v) { var c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
    static byte Gam(double v)
    {
        v = Math.Clamp(v, 0, 1);
        return (byte)Math.Round((v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055) * 255);
    }
}

public static class Wcag
{
    public static double Luminance(string hex)
    {
        static double Ch(int v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        var r = Convert.ToInt32(hex[1..3], 16); var g = Convert.ToInt32(hex[3..5], 16); var b = Convert.ToInt32(hex[5..7], 16);
        return 0.2126 * Ch(r) + 0.7152 * Ch(g) + 0.0722 * Ch(b);
    }

    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}

/// <summary>§5.4: 128 px → OKLab k-means (k = 6) → bg / surface / accent / text / muted, then contrast is guaranteed
/// (text/bg ≥ 7, accent/bg ≥ 3, accentText/accent ≥ 4.5) by moving colors along OKLab L only, so hues survive.</summary>
public static class PaletteExtractor
{
    public const double TextContrast = 7, AccentContrast = 3, AccentTextContrast = 4.5;

    public static Palette FromImage(string path)
    {
        using var img = new MagickImage(path);
        img.AutoOrient();
        img.Resize(new MagickGeometry(128, 128));
        var px = img.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var colors = new Oklab[px.Length / 3];
        for (var i = 0; i < colors.Length; i++) colors[i] = Oklab.FromSrgb(px[i * 3], px[i * 3 + 1], px[i * 3 + 2]);
        return FromColors(colors);
    }

    public static Palette FromColors(IReadOnlyList<Oklab> colors)
    {
        var clusters = KMeans(colors, 6);
        var total = (double)colors.Count;

        // bg: a dark, quiet version of the most common color — the backdrop's own mood, not pure black.
        var dominant = clusters.MaxBy(c => c.Count)!.Center;
        var bg = dominant.WithL(Math.Min(dominant.L, 0.2)).WithChroma(Math.Min(dominant.Chroma, 0.035));
        var surface = bg.WithL(bg.L + 0.05);

        // accent: the most saturated color that is not a speck (≥ 2 % of the image), brightened if needed.
        var accent = clusters.Where(c => c.Count / total >= 0.02).MaxBy(c => c.Center.Chroma * Math.Sqrt(c.Count / total))!.Center;
        if (accent.Chroma < 0.04) accent = new Oklab(0.82, 0.03, 0.08); // a grey picture still needs a warm accent
        accent = accent.WithL(Math.Max(accent.L, 0.62));

        var text = bg.WithL(0.96).WithChroma(0.01);
        var muted = bg.WithL(0.74).WithChroma(0.02);

        var bgHex = bg.Hex;
        var textHex = Separate(text, bgHex, TextContrast, lighter: true);
        var accentHex = Separate(accent, bgHex, AccentContrast, lighter: true);
        var mutedHex = Separate(muted, bgHex, 4.5, lighter: true);
        var accentOk = Oklab.FromSrgb(Convert.ToByte(accentHex[1..3], 16), Convert.ToByte(accentHex[3..5], 16), Convert.ToByte(accentHex[5..7], 16));
        var accentText = Wcag.Contrast(accentHex, "#000000") >= Wcag.Contrast(accentHex, "#ffffff")
            ? Separate(accentOk.WithL(0.18).WithChroma(0.03), accentHex, AccentTextContrast, lighter: false)
            : Separate(accentOk.WithL(0.97).WithChroma(0.01), accentHex, AccentTextContrast, lighter: true);

        return new Palette
        {
            Bg = bgHex, Surface = surface.Hex, Accent = accentHex, AccentText = accentText,
            Text = textHex, Muted = mutedHex, Source = PaletteSource.Auto,
        };
    }

    /// <summary>Moves <paramref name="color"/> along L (away from <paramref name="against"/>) until the WCAG ratio holds.</summary>
    public static string Separate(Oklab color, string against, double ratio, bool lighter)
    {
        for (var step = 0; step <= 100; step++)
        {
            var c = color.WithL(color.L + (lighter ? 0.01 : -0.01) * step);
            if (Wcag.Contrast(c.Hex, against) >= ratio) return c.Hex;
            if (c.L is <= 0 or >= 1) break;
        }
        return lighter ? "#ffffff" : "#000000";
    }

    sealed record Cluster(Oklab Center, int Count);

    /// <summary>Deterministic k-means: seeds are spread over lightness so the same image always gives the same palette.</summary>
    static List<Cluster> KMeans(IReadOnlyList<Oklab> colors, int k)
    {
        var sorted = colors.OrderBy(c => c.L).ToArray();
        var centers = Enumerable.Range(0, k).Select(i => sorted[(int)((i + 0.5) / k * (sorted.Length - 1))]).ToArray();
        var assign = new int[colors.Count];
        for (var iter = 0; iter < 20; iter++)
        {
            var moved = false;
            for (var i = 0; i < colors.Count; i++)
            {
                var best = 0;
                var bestD = double.MaxValue;
                for (var j = 0; j < k; j++)
                {
                    double dl = colors[i].L - centers[j].L, da = colors[i].A - centers[j].A, db = colors[i].B - centers[j].B;
                    var d = dl * dl + da * da + db * db;
                    if (d < bestD) (best, bestD) = (j, d);
                }
                if (assign[i] != best) (assign[i], moved) = (best, true);
            }
            for (var j = 0; j < k; j++)
            {
                var members = Enumerable.Range(0, colors.Count).Where(i => assign[i] == j).Select(i => colors[i]).ToList();
                if (members.Count > 0) centers[j] = new(members.Average(c => c.L), members.Average(c => c.A), members.Average(c => c.B));
            }
            if (!moved && iter > 0) break;
        }
        return Enumerable.Range(0, k).Select(j => new Cluster(centers[j], assign.Count(a => a == j))).Where(c => c.Count > 0).ToList();
    }
}
