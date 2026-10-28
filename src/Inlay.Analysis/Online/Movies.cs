using System.Globalization;
using System.Text.Json;
using Inlay.Core;
using static Inlay.Analysis.Online.JsonRead;

namespace Inlay.Analysis.Online;

/// <summary>OMDb (optional key): IMDb, Rotten Tomatoes and Metacritic scores for a TMDB match, by its IMDb id.
/// OMDb's data is CC BY-NC 4.0, hence the credit on the disc.</summary>
public sealed class Omdb(MetaHttp http, string key)
{
    public const string Attribution = "Puanlar: OMDb API (CC BY-NC 4.0)";

    HttpRequestMessage Get(string imdbId) => MetaHttp.Get($"https://www.omdbapi.com/?apikey={Uri.EscapeDataString(key)}&i={Uri.EscapeDataString(imdbId)}");

    public Task<KeyCheck> Check(CancellationToken ct) => http.Check(Get("tt0111161"), ct);

    /// <summary>Null when OMDb does not know the title or has no score for it ("N/A" everywhere).</summary>
    public async Task<Ratings?> Ratings(string imdbId, CancellationToken ct)
    {
        var doc = await http.Json(Get(imdbId), ct);
        if (doc?.RootElement is not { ValueKind: JsonValueKind.Object } o || Str(o, "Response") != "True") return null;
        var rt = o.TryGetProperty("Ratings", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(r => Str(r, "Source") == "Rotten Tomatoes").Select(r => Str(r, "Value")?.TrimEnd('%')).FirstOrDefault()
            : null;
        var ratings = new Ratings
        {
            Imdb = double.TryParse(Str(o, "imdbRating"), NumberStyles.Float, CultureInfo.InvariantCulture, out var imdb) ? imdb : null,
            RottenTomatoes = int.TryParse(rt, NumberStyles.None, CultureInfo.InvariantCulture, out var tomatoes) ? tomatoes : null,
            Metacritic = int.TryParse(Str(o, "Metascore"), NumberStyles.None, CultureInfo.InvariantCulture, out var meta) ? meta : null,
        };
        return ratings == new Ratings() ? null : ratings;
    }
}

/// <summary>fanart.tv (optional key): HD transparent logos, backgrounds and posters. Movies are filed under their TMDB
/// id, shows under their TheTVDB id. Turkish art first, then English, then art without text; the most liked of each.</summary>
public sealed class FanartTv(MetaHttp http, string key)
{
    public const string Attribution = "Görseller: fanart.tv";

    HttpRequestMessage Get(string kind, string id) =>
        MetaHttp.Get($"https://webservice.fanart.tv/v3/{kind}/{Uri.EscapeDataString(id)}?api_key={Uri.EscapeDataString(key)}");

    public Task<KeyCheck> Check(CancellationToken ct) => http.Check(Get("movies", "550"), ct);

    /// <summary>Fills in the logo, backdrop and poster TMDB had none of; TMDB's own pictures stay.</summary>
    public async Task<MovieMeta> Fill(MovieMeta m, bool series, CancellationToken ct)
    {
        if (m.Logo is not null && m.Backdrop is not null && m.Poster is not null) return m;
        var id = series ? m.TvdbId?.ToString(CultureInfo.InvariantCulture) : m.ExternalId;
        if (id is null) return m;
        var doc = await http.Json(Get(series ? "tv" : "movies", id), ct);
        if (doc?.RootElement is not { ValueKind: JsonValueKind.Object } o) return m;
        var filled = m with
        {
            Logo = m.Logo ?? await http.Image(Best(o, series ? "hdtvlogo" : "hdmovielogo"), ct),
            Backdrop = m.Backdrop ?? await http.Image(Best(o, series ? "showbackground" : "moviebackground"), ct),
            Poster = m.Poster ?? await http.Image(Best(o, series ? "tvposter" : "movieposter"), ct),
        };
        return (filled.Logo, filled.Backdrop, filled.Poster) == (m.Logo, m.Backdrop, m.Poster) ? m : filled with { Credits = [.. m.Credits, Attribution] };
    }

    static string? Best(JsonElement o, string prop) =>
        o.TryGetProperty(prop, out var art) && art.ValueKind == JsonValueKind.Array
            ? art.EnumerateArray()
                .OrderBy(a => Str(a, "lang") switch { "tr" => 0, "en" => 1, null or "00" => 2, _ => 3 })
                .ThenByDescending(a => int.TryParse(Str(a, "likes"), NumberStyles.None, CultureInfo.InvariantCulture, out var likes) ? likes : 0)
                .Select(a => Str(a, "url")).FirstOrDefault(u => u is not null)
            : null;
}

/// <summary>§5.3 film/series chain after the TMDB match: OMDb scores, then fanart.tv for the pictures TMDB lacks.</summary>
public static class MovieLookup
{
    public static async Task<MovieMeta> Enrich(MovieMeta m, bool series, Omdb? omdb, FanartTv? fanart, CancellationToken ct)
    {
        if (omdb is not null && m.ImdbId is { } imdb && await omdb.Ratings(imdb, ct) is { } ratings)
            m = m with { Ratings = ratings, Credits = [.. m.Credits, Omdb.Attribution] };
        return fanart is null ? m : await fanart.Fill(m, series, ct);
    }
}
