using System.Globalization;
using System.Text.Json;
using Inlay.Core;
using static Inlay.Analysis.Online.JsonRead;

namespace Inlay.Analysis.Online;

public sealed record MetaPerson(string Name, string? Role, string? Photo);

/// <summary>Movie/show facts with images already downloaded to the local cache.</summary>
public sealed record MovieMeta
{
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }
    public string? Tagline { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public double? Rating { get; init; }
    public string? Certification { get; init; }
    public string? Poster { get; init; }
    public string? Backdrop { get; init; }
    public string? Logo { get; init; }
    public IReadOnlyList<MetaPerson> Cast { get; init; } = [];
    public IReadOnlyList<(string Name, string Job)> Crew { get; init; } = [];
    public MetaProvider Provider { get; init; }
    public string? ExternalId { get; init; }
    /// <summary>The key OMDb looks scores up by.</summary>
    public string? ImdbId { get; init; }
    /// <summary>Series only: TheTVDB id, the key fanart.tv files shows under.</summary>
    public int? TvdbId { get; init; }
    public Ratings? Ratings { get; init; }
    /// <summary>Attributions of the services that added to TMDB's facts (OMDb, fanart.tv).</summary>
    public IReadOnlyList<string> Credits { get; init; } = [];
    /// <summary>Series only: (season, episode) → title, overview, still, air date.</summary>
    public IReadOnlyDictionary<(int, int), EpisodeMeta> Episodes { get; init; } = new Dictionary<(int, int), EpisodeMeta>();
}

public sealed record EpisodeMeta(string Title, string? Overview, string? Still, DateOnly? AirDate);

/// <summary>TMDB API v3 (§5.3). Accepts a v3 API key or a v4 read access token. Attribution is added to the credits.</summary>
public sealed class Tmdb(MetaHttp http, string key, string language = "tr-TR")
{
    public const string Attribution = "This product uses the TMDB API but is not endorsed or certified by TMDB.";
    const string Api = "https://api.themoviedb.org/3", Images = "https://image.tmdb.org/t/p/";

    HttpRequestMessage Get(string path, string query = "")
    {
        var bearer = key.Length > 40; // v4 tokens are long JWTs, v3 keys are 32 hex chars
        var url = $"{Api}{path}?language={language}{query}{(bearer ? "" : $"&api_key={key}")}";
        return bearer ? MetaHttp.Get(url, ("Authorization", $"Bearer {key}")) : MetaHttp.Get(url);
    }

    public Task<KeyCheck> Check(CancellationToken ct) => http.Check(Get("/configuration"), ct);

    /// <summary>"Bu film hangisi?": the first three matches (§5.3).</summary>
    public Task<IReadOnlyList<MatchCandidate>> SearchMovie(string title, int? year, CancellationToken ct) =>
        Search("/search/movie", title, year is null ? "" : $"&year={year}", "title", "release_date", ct);

    public Task<IReadOnlyList<MatchCandidate>> SearchShow(string title, CancellationToken ct) =>
        Search("/search/tv", title, "", "name", "first_air_date", ct);

    /// <summary>TMDB wants every word of the query in the title, so a file named differently in its last words
    /// ("Bir Böceğin Hayatı" for "Bir Böceğin Yaşamı") finds nothing: it is retried a word shorter, down to two words.
    /// A failed request (offline, 429) is not retried.</summary>
    async Task<IReadOnlyList<MatchCandidate>> Search(string path, string title, string filter, string titleProp, string dateProp, CancellationToken ct)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var n = words.Length; ; n--)
        {
            var doc = await http.Json(Get(path, $"&query={Uri.EscapeDataString(string.Join(' ', words[..n]))}{filter}"), ct);
            var found = Results(doc, titleProp, dateProp);
            if (doc is null || found.Count > 0 || n <= 2) return found;
        }
    }

    static IReadOnlyList<MatchCandidate> Results(JsonDocument? doc, string titleProp, string dateProp) =>
        doc?.RootElement.TryGetProperty("results", out var r) == true
            ? r.EnumerateArray().Take(3).Select(m => new MatchCandidate("tmdb", m.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture),
                Str(m, titleProp) ?? "", Year(Str(m, dateProp)), Str(m, "overview"), Str(m, "poster_path") is { } p ? Images + "w185" + p : null)).ToList()
            : [];

    public async Task<MovieMeta?> Movie(string id, CancellationToken ct)
    {
        var doc = await http.Json(Get($"/movie/{id}", "&append_to_response=credits,images,release_dates&include_image_language=tr,en,null"), ct);
        if (doc is null) return null;
        var m = doc.RootElement;
        return new MovieMeta
        {
            Title = Str(m, "title") ?? "",
            OriginalTitle = Str(m, "original_title"),
            Year = Year(Str(m, "release_date")),
            Overview = Str(m, "overview"),
            Tagline = Str(m, "tagline"),
            Genres = Names(m, "genres"),
            Rating = m.TryGetProperty("vote_average", out var v) && v.ValueKind == JsonValueKind.Number ? Math.Round(v.GetDouble(), 1) : null,
            Certification = Certification(m),
            Poster = await http.Image(Img(m, "poster_path", "w780"), ct),
            Backdrop = await http.Image(Img(m, "backdrop_path", "original"), ct),
            Logo = await http.Image(Logo(m), ct),
            Cast = await People(m, ct),
            Crew = Crew(m),
            Provider = MetaProvider.Tmdb,
            ExternalId = id,
            ImdbId = Str(m, "imdb_id"),
        };
    }

    public async Task<MovieMeta?> Show(string id, IEnumerable<int> seasons, CancellationToken ct)
    {
        var doc = await http.Json(Get($"/tv/{id}", "&append_to_response=credits,images,external_ids&include_image_language=tr,en,null"), ct);
        if (doc is null) return null;
        var s = doc.RootElement;
        var ids = s.TryGetProperty("external_ids", out var x) ? x : default;
        var episodes = new Dictionary<(int, int), EpisodeMeta>();
        foreach (var season in seasons.Distinct())
        {
            var sd = await http.Json(Get($"/tv/{id}/season/{season}"), ct);
            if (sd?.RootElement.TryGetProperty("episodes", out var eps) != true) continue;
            foreach (var e in eps.EnumerateArray())
                episodes[(season, e.GetProperty("episode_number").GetInt32())] = new EpisodeMeta(
                    Str(e, "name") ?? "", Str(e, "overview"), await http.Image(Img(e, "still_path", "w300"), ct),
                    DateOnly.TryParse(Str(e, "air_date"), CultureInfo.InvariantCulture, out var d) ? d : null);
        }
        return new MovieMeta
        {
            Title = Str(s, "name") ?? "",
            OriginalTitle = Str(s, "original_name"),
            Year = Year(Str(s, "first_air_date")),
            Overview = Str(s, "overview"),
            Genres = Names(s, "genres"),
            Rating = s.TryGetProperty("vote_average", out var v) && v.ValueKind == JsonValueKind.Number ? Math.Round(v.GetDouble(), 1) : null,
            Poster = await http.Image(Img(s, "poster_path", "w780"), ct),
            Backdrop = await http.Image(Img(s, "backdrop_path", "original"), ct),
            Logo = await http.Image(Logo(s), ct),
            Cast = await People(s, ct),
            Provider = MetaProvider.Tmdb,
            ExternalId = id,
            ImdbId = ids.ValueKind == JsonValueKind.Object ? Str(ids, "imdb_id") : null,
            TvdbId = ids.ValueKind == JsonValueKind.Object ? Int(ids, "tvdb_id") : null,
            Episodes = episodes,
        };
    }

    async Task<IReadOnlyList<MetaPerson>> People(JsonElement m, CancellationToken ct)
    {
        if (!m.TryGetProperty("credits", out var c) || !c.TryGetProperty("cast", out var cast)) return [];
        var people = new List<MetaPerson>();
        foreach (var p in cast.EnumerateArray().Take(10)) // §5.3: first ten, with photos
            people.Add(new MetaPerson(Str(p, "name") ?? "", Str(p, "character"), await http.Image(Img(p, "profile_path", "w185"), ct)));
        return people;
    }

    static IReadOnlyList<(string, string)> Crew(JsonElement m) =>
        m.TryGetProperty("credits", out var c) && c.TryGetProperty("crew", out var crew)
            ? crew.EnumerateArray().Where(p => Str(p, "job") is "Director" or "Screenplay" or "Writer" or "Original Music Composer")
                .Select(p => (Str(p, "name") ?? "", Str(p, "job") ?? "")).Take(6).ToList()
            : [];

    /// <summary>Turkish rating first, then US (release_dates).</summary>
    static string? Certification(JsonElement m)
    {
        if (!m.TryGetProperty("release_dates", out var rd) || !rd.TryGetProperty("results", out var results)) return null;
        foreach (var country in new[] { "TR", "US" })
            foreach (var r in results.EnumerateArray().Where(r => Str(r, "iso_3166_1") == country))
                foreach (var d in r.GetProperty("release_dates").EnumerateArray())
                    if (Str(d, "certification") is { Length: > 0 } cert) return cert;
        return null;
    }

    static string? Logo(JsonElement m) =>
        m.TryGetProperty("images", out var i) && i.TryGetProperty("logos", out var logos)
            ? logos.EnumerateArray().OrderBy(l => Str(l, "iso_639_1") switch { "tr" => 0, "en" => 1, _ => 2 }).Select(l => Str(l, "file_path")).FirstOrDefault() is { } p
                ? Images + "w500" + p : null
            : null;

    static string? Img(JsonElement e, string prop, string size) => Str(e, prop) is { } p ? Images + size + p : null;
    static IReadOnlyList<string> Names(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var a) ? a.EnumerateArray().Select(g => Str(g, "name") ?? "").Where(n => n.Length > 0).ToList() : [];
    static int? Year(string? date) => date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null;
}
