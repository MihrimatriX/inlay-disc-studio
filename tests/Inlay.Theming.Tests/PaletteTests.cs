using FluentAssertions;
using ImageMagick;
using Inlay.Core;
using Inlay.Theming;
using Xunit;

namespace Inlay.Theming.Tests;

public sealed class PaletteTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-pal-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Image(string name, string spec)
    {
        var p = Path.Combine(_dir, name + ".png");
        using var img = new MagickImage(spec, 400, 300);
        img.Write(p);
        return p;
    }

    public static TheoryData<string> Pictures =>
    [
        "gradient:#0d1a2a-#203040",  // night sky
        "gradient:#f6f2ec-#ffffff",  // nearly white paper
        "gradient:#808080-#8a8a8a",  // flat grey
        "gradient:#ff0000-#00ff00",  // garish
        "plasma:fractal",            // noise
        "gradient:#ffd34d-#ff8a3d",  // bright yellow-orange, a hard case for accent/bg
    ];

    [Theory, MemberData(nameof(Pictures))]
    public void Contrast_is_guaranteed_for_any_picture(string spec)
    {
        var p = PaletteExtractor.FromImage(Image("x", spec));
        Wcag.Contrast(p.Text, p.Bg).Should().BeGreaterThanOrEqualTo(PaletteExtractor.TextContrast, "text on bg");
        Wcag.Contrast(p.Accent, p.Bg).Should().BeGreaterThanOrEqualTo(PaletteExtractor.AccentContrast, "accent on bg");
        Wcag.Contrast(p.AccentText!, p.Accent).Should().BeGreaterThanOrEqualTo(PaletteExtractor.AccentTextContrast, "accentText on accent");
        Wcag.Contrast(p.Muted, p.Bg).Should().BeGreaterThanOrEqualTo(4.5, "muted text on bg");
        p.Source.Should().Be(PaletteSource.Auto);
    }

    [Theory, MemberData(nameof(Pictures))]
    public void Palette_is_valid_for_the_manifest(string spec)
    {
        var p = PaletteExtractor.FromImage(Image("y", spec));
        new[] { p.Bg, p.Surface, p.Accent, p.AccentText!, p.Text, p.Muted }.Should().OnlyContain(h => System.Text.RegularExpressions.Regex.IsMatch(h, "^#[0-9a-f]{6}$"));
    }

    [Fact]
    public void Accent_comes_from_the_vivid_part_of_a_dark_picture()
    {
        var p = Path.Combine(_dir, "poster.png");
        using (var img = new MagickImage(new MagickColor("#101418"), 400, 300))
        {
            new ImageMagick.Drawing.Drawables().FillColor(new MagickColor("#ff6a1a")).Rectangle(150, 100, 250, 200).Draw(img);
            img.Write(p);
        }
        var palette = PaletteExtractor.FromImage(p);
        var accent = Oklab.FromSrgb(Convert.ToByte(palette.Accent[1..3], 16), Convert.ToByte(palette.Accent[3..5], 16), Convert.ToByte(palette.Accent[5..7], 16));
        var orange = Oklab.FromSrgb(0xff, 0x6a, 0x1a);
        Math.Atan2(accent.B, accent.A).Should().BeApproximately(Math.Atan2(orange.B, orange.A), 0.15, "the accent keeps the poster's orange hue");
        Wcag.Luminance(palette.Bg).Should().BeLessThan(0.05, "bg stays dark");
    }

    [Fact]
    public void Same_picture_same_palette()
    {
        var path = Image("z", "plasma:fractal");
        PaletteExtractor.FromImage(path).Should().Be(PaletteExtractor.FromImage(path));
    }

    [Fact]
    public void Oklab_round_trips_srgb()
    {
        foreach (var (r, g, b) in new (byte, byte, byte)[] { (0, 0, 0), (255, 255, 255), (255, 90, 31), (12, 200, 99) })
            Oklab.FromSrgb(r, g, b).ToSrgb().Should().Be((r, g, b));
    }

    [Fact]
    public void Wcag_contrast_matches_known_values()
    {
        Wcag.Contrast("#000000", "#ffffff").Should().BeApproximately(21, 0.01);
        Wcag.Contrast("#767676", "#ffffff").Should().BeApproximately(4.54, 0.01);
    }
}
