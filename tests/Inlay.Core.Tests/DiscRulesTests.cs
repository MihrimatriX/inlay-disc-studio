using System.Text.Json;
using FluentAssertions;
using Inlay.Core;
using Xunit;

namespace Inlay.Core.Tests;

public class DiscRulesTests
{
    [Theory]
    [InlineData(DiscMedia.Cd700, 737_280_000)]
    [InlineData(DiscMedia.Dvd5, 4_700_372_992)]
    [InlineData(DiscMedia.Dvd9, 8_547_991_552)]
    public void Capacity_matches_the_table(DiscMedia media, long bytes) => MediaCapacity.Bytes(media).Should().Be(bytes);

    [Theory]
    [InlineData(700_000_000L, DiscMedia.Cd700)]
    [InlineData(730_000_000L, DiscMedia.Dvd5)]   // over CD once the 1.5 % file system share is reserved
    [InlineData(4_700_000_000L, DiscMedia.Dvd9)] // a full DVD-5's worth of content needs DVD DL
    public void Smallest_medium_reserves_file_system_overhead(long content, DiscMedia expected) =>
        MediaCapacity.Smallest(content).Should().Be(expected);

    [Fact]
    public void Nothing_fits_beyond_dvd_dl() => MediaCapacity.Smallest(9_000_000_000L).Should().BeNull();

    [Theory]
    [InlineData("Kuzey Rüzgârı", "KUZEY_RUZGARI")]
    [InlineData("Işıklı İstanbul Şarkıları", "ISIKLI_ISTANBUL_SARKILARI")]
    [InlineData("  Ofis — Günlükleri (2026)!  ", "OFIS_GUNLUKLERI_2026")]
    [InlineData("çğ", "CG")]
    [InlineData("—", "INLAY_DISC")]
    [InlineData("Bir Çok Uzun Başlıklı Film Koleksiyonu Cilt İki", "BIR_COK_UZUN_BASLIKLI_FILM_KOLEK")]
    public void Label_is_ascii_upper_and_short(string title, string label) => DiscLabel.FromTitle(title).Should().Be(label);

    [Fact]
    public void Iso9660_label_is_16_chars() => DiscLabel.Iso9660("BIR_COK_UZUN_BASLIKLI").Should().Be("BIR_COK_UZUN_BAS");

    [Fact]
    public void Files_of_4_GiB_force_udf_only()
    {
        DiscFileSystem.Choose([10, MediaCapacity.FourGiB - 1]).Should().Be(DiscFileSystems.Iso9660 | DiscFileSystems.Joliet | DiscFileSystems.Udf);
        DiscFileSystem.Choose([10, MediaCapacity.FourGiB]).Should().Be(DiscFileSystems.Udf);
    }

    [Fact]
    public void Joliet_violations_check_each_segment()
    {
        var longName = new string('a', 65) + ".mp4";
        DiscFileSystem.JolietViolations(["Film/ok.mp4", $"Film/{longName}", $"{new string('b', 64)}/x"])
            .Should().Equal($"Film/{longName}");
    }

    static DiscManifest Sample(string name) =>
        JsonSerializer.Deserialize(File.ReadAllText($"samples/manifest.{name}.json"), InlayJson.Default.DiscManifest)!;

    [Fact]
    public void Autorun_points_at_launcher_and_icon()
    {
        var inf = Autorun.Build(Sample("bundle"));
        inf.Should().Be(
            "[autorun]\r\nopen=Baslat.exe\r\nicon=.disc\\icon.ico\r\nlabel=Oyunumuz — Kurulum Diski\r\n" +
            "action=Programları kur\r\nshellexecute=Baslat.exe\r\n");
    }

    [Theory]
    [InlineData("mixed", "Menüyü aç")]
    [InlineData("series", "Diziyi izle")]
    [InlineData("bundle", "Programları kur")]
    public void Autorun_action_follows_home_view(string sample, string action) =>
        Autorun.ActionText(Sample(sample)).Should().Be(action);

    [Fact]
    public void Autorun_action_for_single_item_follows_item_type()
    {
        var m = Sample("mixed");
        var movie = m.Items.OfType<Movie>().First();
        Autorun.ActionText(m with { Disc = m.Disc with { Home = new DiscHome { View = HomeView.Item, Item = movie.Id } } })
            .Should().Be("Filmi izle");
        Autorun.ActionText(m with { Disc = m.Disc with { Locale = "en-US", Home = new DiscHome { View = HomeView.Item, Item = movie.Id } } })
            .Should().Be("Watch the movie");
    }

    [Fact]
    public void Autorun_label_is_cut_at_32_chars()
    {
        var m = Sample("bundle");
        Autorun.Build(m with { Disc = m.Disc with { Title = new string('x', 40) } }).Should().Contain($"label={new string('x', 32)}\r\n");
    }

    [Fact]
    public void Every_text_key_exists_in_both_languages() =>
        Text.Table("en-US").Keys.Should().BeEquivalentTo(Text.Table("tr-TR").Keys);

    [Theory]
    [InlineData("Film/a.mp4", true)]
    [InlineData(".disc/icon.ico", true)]
    [InlineData("../a.exe", false)]
    [InlineData("a/../../a.exe", false)]
    [InlineData("/a.exe", false)]
    [InlineData("C:/a.exe", false)]
    [InlineData("c:a.exe", false)]
    [InlineData("https://x/a", false)]
    [InlineData("a/b:stream", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RelPath_validation(string? path, bool valid) => RelPath.IsValid(path).Should().Be(valid);

    [Fact]
    public void RelPath_resolves_under_root_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "disc");
        RelPath.Resolve(root, "Setup/setup.exe").Should().Be(Path.Combine(root, "Setup", "setup.exe"));
        RelPath.Resolve(root, "..\\x.exe").Should().BeNull();
        RelPath.Resolve(root, "Setup\\..\\..\\x.exe").Should().BeNull();
        RelPath.Resolve(root, "\\\\server\\share\\x.exe").Should().BeNull();
    }

    [Fact]
    public void RelPath_resolves_under_a_drive_root()
    {
        const string root = @"E:\";
        RelPath.Resolve(root, ".disc/manifest.json").Should().BeEquivalentTo(@"E:\.disc\manifest.json");
        RelPath.Resolve(root, "../x.exe").Should().BeNull();
    }
}
