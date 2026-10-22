using FluentAssertions;
using Inlay.Analysis;
using Xunit;

namespace Inlay.Analysis.Tests;

public class NamesTests
{
    [Theory]
    [InlineData("Kuzey.Ruzgari.2023.1080p.BluRay.x264-GRP.mkv", "Kuzey Ruzgari", 2023)]
    [InlineData("Inception (2010) [1080p].mp4", "Inception", 2010)]
    [InlineData("Kuzey Rüzgârı.mp4", "Kuzey Rüzgârı", null)]
    [InlineData("2001.A.Space.Odyssey.1968.2160p.UHD.mkv", "2001 A Space Odyssey", 1968)]
    [InlineData("1917.2019.HDR10.mkv", "1917", 2019)]
    [InlineData("Babam.ve.Oglum.2005.WEB-DL.AAC.mp4", "Babam ve Oglum", 2005)]
    public void Movie_titles_drop_release_noise(string name, string title, int? year) =>
        Names.Movie(name).Should().Be(new ParsedTitle(title, year));

    [Theory]
    [InlineData("Ofis.Gunlukleri.S01E03.720p.mkv", 1, 3)]
    [InlineData("ofis s2e10.mp4", 2, 10)]
    [InlineData("Ofis 1x05.mkv", 1, 5)]
    [InlineData("Ofis/Sezon 2/Bölüm 4.mp4", 2, 4)]
    [InlineData("Ofis/Season 3/Episode 12.mkv", 3, 12)]
    [InlineData("Ofis/Bolum 7.mp4", 1, 7)]
    public void Episode_patterns(string path, int season, int episode) =>
        Names.Episode(path).Should().Be(new EpisodeRef(season, episode));

    [Theory]
    [InlineData("Kuzey Rüzgârı.mp4")]
    [InlineData("1917.2019.mkv")]
    [InlineData("Fragman.mp4")]
    public void Movies_are_not_episodes(string path) => Names.Episode(path).Should().BeNull();

    [Fact]
    public void Series_title_comes_before_the_marker() => Names.SeriesTitle("Ofis.Gunlukleri.S01E03.720p.mkv").Should().Be("Ofis Gunlukleri");

    [Theory]
    [InlineData("setup_the_witcher_3_wild_hunt_v4.04_(gog).exe", "The Witcher 3 Wild Hunt")]
    [InlineData("Hollow.Knight.v1.5.78-GOG", "Hollow Knight")]
    [InlineData("Celeste-FitGirl", "Celeste")]
    [InlineData("Oyunumuz Kurulum", "Oyunumuz")]
    [InlineData("setup.exe", "")]
    public void Game_names_lose_installer_noise(string name, string clean) => Names.Game(name).Should().Be(clean);

    [Theory]
    [InlineData("mv", "Kuzey Rüzgârı", "mv-kuzey-ruzgari")]
    [InlineData("al", "Işık & Gölge — Vol. 2", "al-isik-golge-vol-2")]
    [InlineData("ap", "!!!", "ap-x")]
    public void Ids_are_manifest_safe(string prefix, string title, string id) => Names.Id(prefix, title).Should().Be(id);

    [Fact]
    public void Ids_are_capped_at_64()
    {
        var id = Names.Id("mv", new string('a', 100));
        id.Should().HaveLength(64).And.MatchRegex("^[a-z0-9][a-z0-9._-]{0,63}$");
    }

    [Theory]
    [InlineData("Kuzey Rüzgârı: Yönetmen Kurgusu?.mkv", "Kuzey Rüzgârı Yönetmen Kurgusu .mkv")]
    [InlineData("a|b<c>.txt", "a b c .txt")]
    [InlineData("klasör.", "klasör")]
    public void Disc_segments_are_cleaned(string name, string clean) => Names.DiscSegment(name).Should().Be(clean);

    [Fact]
    public void Long_disc_segments_keep_their_extension()
    {
        var s = Names.DiscSegment(new string('x', 80) + ".flac");
        s.Should().HaveLength(64).And.EndWith(".flac");
    }
}
