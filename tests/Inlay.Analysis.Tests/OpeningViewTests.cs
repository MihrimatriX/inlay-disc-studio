using FluentAssertions;
using Inlay.Analysis;
using Inlay.Core;
using Xunit;

namespace Inlay.Analysis.Tests;

/// <summary>§7.0 table, one test per row.</summary>
public class OpeningViewTests
{
    static MediaFile Media => new() { File = "x.mp4", SizeBytes = 1, Playback = new Playback { Engine = PlaybackEngine.Html } };
    static Movie Movie(string id) => new() { Id = id, Title = id, Media = Media };
    static Album Album(string id) => new() { Id = id, Title = id, Tracks = [new Track { Id = id + "-1", No = 1, Title = "t", Media = Media }] };
    static Gallery Gallery(string id) => new() { Id = id, Title = id, Photos = [new Photo { Id = id + "-p", File = "a.webp", Thumb = "t.webp", Width = 1, Height = 1 }] };
    static Series Series(string id) => new() { Id = id, Title = id, Seasons = [new Season { No = 1, Episodes = [new Episode { Id = id + "-e1", No = 1, Title = "e", Media = Media }] }] };
    static App App(string id, AppCategory c = AppCategory.Game) => new() { Id = id, Title = id, Category = c };
    static FileItem Doc(string id) => new() { Id = id, Title = id, Path = "a.pdf" };

    static DiscHome Home(params DiscItem[] items) => OpeningView.Decide(items);

    [Fact] public void Mixed_content_opens_the_menu() => Home(Movie("m"), Album("a")).View.Should().Be(HomeView.Menu);
    [Fact] public void One_movie_opens_its_page() => Home(Movie("m")).Should().Be(new DiscHome { View = HomeView.Item, Item = "m" });
    [Fact] public void Movies_open_the_collection() => Home(Movie("m1"), Movie("m2")).Should().Be(new DiscHome { View = HomeView.Collection, Kind = CollectionKind.Movie });
    [Fact] public void A_series_opens_the_series_page() => Home(Series("s")).Should().Be(new DiscHome { View = HomeView.Series, Item = "s" });
    [Fact] public void One_album_opens_its_page() => Home(Album("a")).Should().Be(new DiscHome { View = HomeView.Item, Item = "a" });
    [Fact] public void Albums_open_the_collection() => Home(Album("a1"), Album("a2")).Should().Be(new DiscHome { View = HomeView.Collection, Kind = CollectionKind.Album });
    [Fact] public void One_game_opens_its_page_and_prerequisites_hide() => Home(App("g"), App("vc", AppCategory.Runtime)).Should().Be(new DiscHome { View = HomeView.Item, Item = "g" });
    [Fact] public void Several_installers_open_the_bundle() => Home(App("g"), App("t", AppCategory.Tool), App("vc", AppCategory.Runtime)).View.Should().Be(HomeView.Bundle);
    [Fact] public void Only_photos_open_the_gallery() => Home(Gallery("g")).Should().Be(new DiscHome { View = HomeView.Item, Item = "g" });
    [Fact] public void Galleries_open_the_collection() => Home(Gallery("g1"), Gallery("g2")).Kind.Should().Be(CollectionKind.Gallery);
    [Fact] public void One_document_opens_it() => Home(Doc("d")).Should().Be(new DiscHome { View = HomeView.Item, Item = "d" });
    [Fact] public void Several_documents_open_a_list() => Home(Doc("d1"), Doc("d2")).View.Should().Be(HomeView.Menu);
    [Fact] public void Only_prerequisites_still_install_as_a_bundle() => Home(App("vc", AppCategory.Runtime)).View.Should().Be(HomeView.Bundle);

    [Fact]
    public void Decisions_match_the_samples()
    {
        foreach (var name in new[] { "mixed", "series", "bundle" })
        {
            var m = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText($"samples/manifest.{name}.json"), InlayJson.Default.DiscManifest)!;
            OpeningView.Decide(m.Items).View.Should().Be(m.Disc.Home.View, name);
        }
    }
}
