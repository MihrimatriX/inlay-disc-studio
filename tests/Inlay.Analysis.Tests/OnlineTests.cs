using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Inlay.Analysis.Online;
using Inlay.Core;
using Xunit;

namespace Inlay.Analysis.Tests;

/// <summary>Recorded-shape responses; nothing here touches the network.</summary>
sealed class FakeApi : HttpMessageHandler
{
    public readonly List<string> Calls = [];
    readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpResponseMessage> Reply)> _routes = [];

    public FakeApi On(string urlPart, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((r => r.RequestUri!.ToString().Contains(urlPart, StringComparison.Ordinal), () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls.Add($"{request.Method} {request.RequestUri}");
        if (request.RequestUri!.Host.StartsWith("image", StringComparison.Ordinal) || request.RequestUri.AbsolutePath.EndsWith(".jpg") || request.RequestUri.AbsolutePath.EndsWith(".png"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]) });
        var route = _routes.LastOrDefault(r => r.Match(request));
        return Task.FromResult(route.Reply?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public sealed class OnlineTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-online-").FullName;
    readonly string _oldRoot = InlayFolders.Root;
    readonly FakeApi _api = new();
    readonly HttpClient _client;
    readonly MetaHttp _http;

    public OnlineTests()
    {
        InlayFolders.Root = _dir;
        _client = new HttpClient(_api);
        _http = new MetaHttp(_client);
    }

    public void Dispose()
    {
        InlayFolders.Root = _oldRoot;
        Directory.Delete(_dir, recursive: true);
    }

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    const string TmdbSearch = """
        {"page":1,"results":[
          {"id":101,"title":"Kuzey Rüzgârı","release_date":"2023-11-03","poster_path":"/p1.jpg","overview":"Dört kişi."},
          {"id":102,"title":"Kuzey","release_date":"2019-01-01","poster_path":null,"overview":""},
          {"id":103,"title":"Rüzgâr","release_date":"","overview":"x"},
          {"id":104,"title":"Fazla","release_date":"2000-01-01"}]}
        """;

    const string TmdbMovie = """
        {"id":101,"title":"Kuzey Rüzgârı","original_title":"North Wind","release_date":"2023-11-03","overview":"Kayıp bir istasyonu arayan dört kişi.",
         "tagline":"Dağ bekler.","vote_average":7.36,"imdb_id":"tt0000101","genres":[{"id":12,"name":"Macera"},{"id":18,"name":"Dram"}],
         "poster_path":"/poster.jpg","backdrop_path":"/backdrop.jpg",
         "images":{"logos":[{"file_path":"/en.png","iso_639_1":"en"},{"file_path":"/tr.png","iso_639_1":"tr"}]},
         "release_dates":{"results":[{"iso_3166_1":"US","release_dates":[{"certification":"PG-13"}]},{"iso_3166_1":"TR","release_dates":[{"certification":""},{"certification":"13+"}]}]},
         "credits":{"cast":[{"name":"Mert Demir","character":"Kerem","profile_path":"/m.jpg"},{"name":"Ece Ak","character":"Leyla","profile_path":null}],
                    "crew":[{"name":"Ayşe Kaya","job":"Director"},{"name":"Biri","job":"Grip"}]}}
        """;

    [Fact]
    public async Task Tmdb_search_offers_three_matches()
    {
        _api.On("/search/movie", TmdbSearch);
        var matches = await new Tmdb(_http, "0123456789abcdef0123456789abcdef").SearchMovie("Kuzey Rüzgârı", 2023, Ct);
        matches.Should().HaveCount(3);
        matches[0].Should().Be(new MatchCandidate("tmdb", "101", "Kuzey Rüzgârı", 2023, "Dört kişi.", "https://image.tmdb.org/t/p/w185/p1.jpg"));
        matches[2].Year.Should().BeNull();
        _api.Calls.Single().Should().Contain("api_key=0123456789abcdef0123456789abcdef").And.Contain("year=2023").And.Contain("language=tr-TR");
    }

    [Fact]
    public async Task Tmdb_search_drops_trailing_words_until_something_matches()
    {
        _api.On("/search/movie", TmdbSearch).On("Efsanesi", """{"page":1,"results":[]}""");
        var matches = await new Tmdb(_http, "k").SearchMovie("Kuzey Rüzgârı Efsanesi", null, Ct);
        matches[0].Title.Should().Be("Kuzey Rüzgârı");
        _api.Calls.Should().HaveCount(2);

        _api.On("/search/tv", "{}", HttpStatusCode.TooManyRequests);
        (await new Tmdb(_http, "k").SearchShow("Bir İki Üç Dört", Ct)).Should().BeEmpty();
        _api.Calls.Should().HaveCount(3, "a failed request is not retried shorter");
    }

    [Fact]
    public async Task Tmdb_v4_tokens_go_in_the_authorization_header()
    {
        var seen = "";
        var handler = new Capture(r => seen = r.Headers.Authorization?.ToString() ?? "");
        await new Tmdb(new MetaHttp(new HttpClient(handler)), new string('t', 200)).SearchMovie("x", null, Ct);
        seen.Should().Be("Bearer " + new string('t', 200));
    }

    [Fact]
    public async Task Tmdb_details_download_images_and_prefer_turkish()
    {
        _api.On("/movie/101", TmdbMovie);
        var m = (await new Tmdb(_http, "k").Movie("101", Ct))!;
        m.Should().Match<MovieMeta>(x => x.Title == "Kuzey Rüzgârı" && x.OriginalTitle == "North Wind" && x.Year == 2023 && x.Rating == 7.4
            && x.Certification == "13+" && x.Provider == MetaProvider.Tmdb && x.ExternalId == "101" && x.ImdbId == "tt0000101");
        m.Genres.Should().Equal("Macera", "Dram");
        m.Cast.Select(c => (c.Name, c.Role)).Should().Equal(("Mert Demir", "Kerem"), ("Ece Ak", "Leyla"));
        m.Cast[0].Photo.Should().NotBeNull().And.Match(p => File.Exists(p));
        m.Cast[1].Photo.Should().BeNull();
        m.Crew.Should().Equal(("Ayşe Kaya", "Director"));
        new[] { m.Poster, m.Backdrop, m.Logo }.Should().OnlyContain(p => p != null && p.StartsWith(InlayFolders.ImageCache));
        _api.Calls.Should().Contain(c => c.EndsWith("/t/p/w500/tr.png"), "the Turkish logo wins");
        _api.Calls.Should().Contain(c => c.EndsWith("/t/p/original/backdrop.jpg"));
    }

    [Fact]
    public async Task Responses_are_cached_and_failures_are_quiet()
    {
        _api.On("/search/movie", TmdbSearch);
        var tmdb = new Tmdb(_http, "k");
        await tmdb.SearchMovie("a", null, Ct);
        await tmdb.SearchMovie("a", null, Ct);
        _api.Calls.Should().ContainSingle("the second answer comes from %LOCALAPPDATA%\\Inlay\\cache\\meta");

        _api.On("/search/tv", "{}", HttpStatusCode.TooManyRequests);
        (await tmdb.SearchShow("b", Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Tmdb_shows_bring_the_ids_omdb_and_fanart_need()
    {
        _api.On("/tv/7", """{"name":"Dizi","external_ids":{"imdb_id":"tt0000007","tvdb_id":70}}""");
        var s = (await new Tmdb(_http, "k").Show("7", [], Ct))!;
        (s.ImdbId, s.TvdbId).Should().Be(("tt0000007", 70));
        _api.Calls.Single().Should().Contain("append_to_response=credits,images,external_ids");
    }

    [Fact]
    public async Task Omdb_scores_and_fanart_fill_only_what_tmdb_lacks()
    {
        _api.On("omdbapi.com", """
                {"Title":"North Wind","imdbRating":"7.8","Metascore":"N/A","Response":"True",
                 "Ratings":[{"Source":"Internet Movie Database","Value":"7.8/10"},{"Source":"Rotten Tomatoes","Value":"91%"}]}
                """)
            .On("webservice.fanart.tv/v3/movies/101", """
                {"hdmovielogo":[{"url":"https://assets.fanart.tv/logo-en.png","lang":"en","likes":"9"},{"url":"https://assets.fanart.tv/logo-tr.png","lang":"tr","likes":"1"}],
                 "moviebackground":[{"url":"https://assets.fanart.tv/bg.jpg","lang":"","likes":"3"}]}
                """);
        var tmdb = new MovieMeta { Title = "Kuzey Rüzgârı", Provider = MetaProvider.Tmdb, ExternalId = "101", ImdbId = "tt0000101", Backdrop = "tmdb-backdrop.jpg" };
        var m = await MovieLookup.Enrich(tmdb, series: false, new Omdb(_http, "o"), new FanartTv(_http, "f"), Ct);
        m.Ratings.Should().Be(new Ratings { Imdb = 7.8, RottenTomatoes = 91 }, "Metacritic is N/A");
        m.Backdrop.Should().Be("tmdb-backdrop.jpg", "TMDB's own picture stays");
        m.Logo.Should().StartWith(InlayFolders.ImageCache);
        m.Poster.Should().BeNull("fanart.tv has no poster for it either");
        _api.Calls.Should().Contain(c => c.EndsWith("/logo-tr.png"), "the Turkish logo wins over a more liked English one");
        _api.Calls.Should().NotContain(c => c.EndsWith("/bg.jpg"));
        m.Credits.Should().Equal(Omdb.Attribution, FanartTv.Attribution);
    }

    [Fact]
    public async Task Unknown_titles_get_no_scores_and_shows_without_a_tvdb_id_skip_fanart()
    {
        _api.On("omdbapi.com", """{"Response":"False","Error":"Incorrect IMDb ID."}""");
        var show = new MovieMeta { Title = "Dizi", Provider = MetaProvider.Tmdb, ExternalId = "7", ImdbId = "tt0000007" };
        var m = await MovieLookup.Enrich(show, series: true, new Omdb(_http, "o"), new FanartTv(_http, "f"), Ct);
        m.Should().BeSameAs(show);
        _api.Calls.Should().ContainSingle().Which.Should().Contain("omdbapi.com");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, KeyCheck.Ok)]
    [InlineData(HttpStatusCode.Unauthorized, KeyCheck.Rejected)]
    [InlineData(HttpStatusCode.BadRequest, KeyCheck.Rejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, KeyCheck.Unreachable)]
    public async Task Key_tests_tell_a_refused_key_from_an_unreachable_service(HttpStatusCode status, KeyCheck expected)
    {
        _api.On("/configuration", "{}", status);
        var tmdb = new Tmdb(_http, "k");
        (await tmdb.Check(Ct)).Should().Be(expected);
        (await tmdb.Check(Ct)).Should().Be(expected);
        _api.Calls.Should().HaveCount(2, "a key test is never answered from the cache");
    }

    [Fact]
    public async Task Igdb_reuses_its_token_and_finds_the_steam_id()
    {
        var tokenCalls = 0;
        var handler = new Capture(r => { if (r.RequestUri!.Host == "id.twitch.tv") tokenCalls++; }, req => req.RequestUri!.Host switch
        {
            "id.twitch.tv" => """{"access_token":"tok","expires_in":5000000,"token_type":"bearer"}""",
            "api.igdb.com" => """
                [{"id":1942,"name":"The Witcher 3: Wild Hunt","first_release_date":1431993600,"summary":"Geralt.",
                  "genres":[{"name":"Role-playing (RPG)"}],"cover":{"image_id":"co1wyy"},"artworks":[{"image_id":"ar1"}],
                  "screenshots":[{"image_id":"s1"},{"image_id":"s2"},{"image_id":"s3"},{"image_id":"s4"}],
                  "involved_companies":[{"company":{"name":"Publisher"},"developer":false},{"company":{"name":"CD PROJEKT RED"},"developer":true}],
                  "external_games":[{"uid":"1207664643","external_game_source":5},{"uid":"292030","external_game_source":1}]}]
                """,
            _ => null,
        });
        var client = new HttpClient(handler);
        var igdb = new Igdb(new MetaHttp(client), client, "cid", "secret");
        var found = await igdb.Search("The Witcher 3", Ct);
        found.Single().Should().Be(new MatchCandidate("igdb", "1942", "The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED", "https://images.igdb.com/igdb/image/upload/t_cover_small/co1wyy.jpg"));
        var (meta, steam) = (await igdb.Game("1942", Ct))!.Value;
        steam.Should().Be(292030);
        meta.Screenshots.Should().HaveCount(3);
        meta.Credits.Should().Equal(Igdb.Attribution);
        tokenCalls.Should().Be(1);
        handler.Requests.Where(r => r.Host == "api.igdb.com").Should().OnlyContain(r => r.Auth == "Bearer tok" && r.ClientId == "cid");
        handler.Bodies.Should().Contain(b => b.Contains("where platforms = (6)") && b.StartsWith("search \"The Witcher 3\""));
    }

    [Fact]
    public async Task Token_bucket_allows_four_per_second()
    {
        var bucket = new TokenBucket(4);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 4; i++) await bucket.Wait(Ct);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(300));
        await bucket.Wait(Ct);
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public void Steam_requirements_parse_turkish_labels_and_prefer_recommended()
    {
        using var doc = JsonDocument.Parse("""
            {"minimum":"<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>İşletim Sistemi:</strong> 64-bit Windows 10<br></li><li><strong>İşlemci:</strong> Intel Core i5-2500K<br></li><li><strong>Bellek:</strong> 6 GB RAM<br></li><li><strong>Ekran Kartı:</strong> GTX 660<br></li><li><strong>DirectX:</strong> Sürüm 11<br></li><li><strong>Depolama:</strong> 50 GB kullanılabilir alan</li></ul>",
             "recommended":"<strong>Önerilen:</strong><br><ul><li><strong>Bellek:</strong> 8 GB RAM<br></li><li><strong>Ekran Kartı:</strong> GTX 1060 &amp; üstü<br></li></ul>"}
            """);
        SteamStore.Requirements(doc.RootElement).Should().BeEquivalentTo(new Requirements
        {
            Os = "64-bit Windows 10", Cpu = "Intel Core i5-2500K", RamGb = 8, Gpu = "GTX 1060 & üstü", Directx = "Sürüm 11", DiskGb = 50,
        });
    }

    [Theory]
    [InlineData("Memory: 512 MB RAM", 0.5)]
    [InlineData("16 GB RAM", 16.0)]
    [InlineData("1,5 GB", 1.5)]
    [InlineData("no size", null)]
    public void Sizes_become_gigabytes(string text, double? gb) => SteamStore.Gb(text).Should().Be(gb);

    [Fact]
    public async Task Game_chain_merges_with_hero_priority_and_attribution()
    {
        var handler = new Capture(null, req => req.RequestUri!.ToString() switch
        {
            var u when u.Contains("id.twitch.tv") => """{"access_token":"t","expires_in":100000}""",
            var u when u.Contains("api.igdb.com") => """[{"id":7,"name":"Celeste","first_release_date":1516838400,"artworks":[{"image_id":"art"}],"external_games":[{"uid":"504230","category":1}]}]""",
            var u when u.Contains("appdetails") => """{"504230":{"success":true,"data":{"name":"Celeste","short_description":"Dağa tırman.","header_image":"https://cdn.steam/header.jpg","genres":[{"description":"Platform"}],"developers":["Maddy Makes Games"],"pc_requirements":{"minimum":"<li><strong>Bellek:</strong> 2 GB RAM<br></li>"}}}}""",
            var u when u.Contains("/games/steam/504230") => """{"success":true,"data":{"id":555}}""",
            var u when u.Contains("/logos/game/555") => """{"success":true,"data":[{"url":"https://cdn.sgdb/logo-low.png","score":1},{"url":"https://cdn.sgdb/logo.png","score":9}]}""",
            var u when u.Contains("/heroes/game/555") => """{"success":true,"data":[]}""",
            _ => null,
        });
        var client = new HttpClient(handler);
        var http = new MetaHttp(client);
        var meta = (await GameLookup.Resolve(new MatchCandidate("igdb", "7", "Celeste", 2018, null, null), "Celeste",
            new Igdb(http, client, "c", "s"), new SteamStore(http), new SteamGridDb(http, "key"), Ct))!;
        meta.Should().Match<GameMeta>(m => m.Title == "Celeste" && m.Year == 2018 && m.Summary == "Dağa tırman." && m.Developer == "Maddy Makes Games"
            && m.Requirements!.RamGb == 2 && m.SteamAppId == 504230);
        meta.Logo.Should().NotBeNull();
        handler.Requests.Should().Contain(r => r.Url.EndsWith("/logo.png"), "the best scored logo");
        handler.Requests.Should().Contain(r => r.Url.EndsWith("/t_1080p/art.jpg"), "no SteamGridDB hero, so the IGDB artwork is the hero");
        meta.Credits.Should().Equal(Igdb.Attribution, "Steam", SteamGridDb.Attribution);
    }

    [Fact]
    public void Keys_are_encrypted_at_rest()
    {
        var path = Path.Combine(_dir, "keys.dat");
        var store = new KeyStore(path);
        store.Set("tmdb", "s3cr3t-value");
        File.ReadAllText(path).Should().NotContain("s3cr3t");
        new KeyStore(path).Get("tmdb").Should().Be("s3cr3t-value");
        store.Set("tmdb", null);
        new KeyStore(path).Get("tmdb").Should().BeNull();
    }

    /// <summary>Records requests; answers JSON by URL (images get a tiny JPEG).</summary>
    sealed class Capture(Action<HttpRequestMessage>? onRequest, Func<HttpRequestMessage, string?>? reply = null) : HttpMessageHandler
    {
        public readonly List<(string Host, string Url, string? Auth, string? ClientId)> Requests = [];
        public readonly List<string> Bodies = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            onRequest?.Invoke(r);
            Requests.Add((r.RequestUri!.Host, r.RequestUri.ToString(), r.Headers.Authorization?.ToString(),
                r.Headers.TryGetValues("Client-ID", out var c) ? c.First() : null));
            if (r.Content is not null) Bodies.Add(await r.Content.ReadAsStringAsync(ct));
            if (Path.GetExtension(r.RequestUri.AbsolutePath) is ".jpg" or ".png")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            var json = reply?.Invoke(r);
            return json is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
