using FluentAssertions;
using Inlay.Analysis;
using Inlay.Core;
using Xunit;

namespace Inlay.Analysis.Tests;

public sealed class CapacityTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-cap-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static MediaFile Media(string file, string? original = null) =>
        new() { File = file, SizeBytes = 1, Playback = new Playback { Engine = PlaybackEngine.Html, Original = original } };

    DiscDraft Draft(DiscMedia media, long main, long trailer, long original)
    {
        var movie = new Movie { Id = "mv-a", Title = "A", Media = Media("Film/a.mp4", original > 0 ? "Film/original/a.mkv" : null), Trailer = Media("Film/Fragman.mp4") };
        var m = new DiscManifest
        {
            Version = 1,
            Disc = new Disc { Label = "A", Title = "A", Media = media, CreatedAt = DateTimeOffset.UnixEpoch, Builder = "t", Home = new DiscHome { View = HomeView.Item, Item = "mv-a" } },
            Theme = new ThemeRef { Package = "p", Version = "1.0.0", Palette = Composer.DefaultPalette },
            Sections = [new Section { Id = "s", Kind = SectionKind.Movie, Title = "F", Items = ["mv-a"] }],
            Items = [movie], Actions = [],
        };
        var files = new Dictionary<string, FileJob> { ["Film/a.mp4"] = new CopyJob("a", main), ["Film/Fragman.mp4"] = new CopyJob("t", trailer) };
        if (original > 0) files["Film/original/a.mkv"] = new CopyJob("o", original);
        return new DiscDraft(m, files);
    }

    [Fact]
    public void A_disc_that_fits_needs_no_suggestion() => Capacity.Suggest(Draft(DiscMedia.Dvd5, 3_000_000_000, 86_000_000, 0)).Should().BeEmpty();

    [Fact]
    public void Over_by_a_trailer_suggests_dvd_dl_first_then_the_trailer()
    {
        var s = Capacity.Suggest(Draft(DiscMedia.Dvd5, 4_550_000_000, 86_000_000, 0));
        s[0].Should().Be(new Suggestion(SuggestionKind.BiggerMedia, 0, Media: DiscMedia.Dvd9));
        s.Should().Contain(new Suggestion(SuggestionKind.DropTrailer, 86_000_000, "mv-a"));
    }

    [Fact]
    public void Kept_originals_are_offered_for_removal()
    {
        var s = Capacity.Suggest(Draft(DiscMedia.Cd700, 400_000_000, 10_000_000, 600_000_000));
        s.Should().Contain(new Suggestion(SuggestionKind.DropOriginals, 600_000_000));
        s.Should().Contain(x => x.Kind == SuggestionKind.BiggerMedia && x.Media == DiscMedia.Dvd5);
    }
}
