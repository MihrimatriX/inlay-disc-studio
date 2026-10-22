using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inlay.Core;

namespace Inlay.Analysis.Online;

public sealed record GameMeta
{
    public required string Title { get; init; }
    public int? Year { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public string? Developer { get; init; }
    public string? Cover { get; init; }
    public string? Hero { get; init; }
    public string? Logo { get; init; }
    public IReadOnlyList<string> Screenshots { get; init; } = [];
    /// <summary>Only from Steam's structured requirements; never guessed (§5.3).</summary>
    public Requirements? Requirements { get; init; }
    public IReadOnlyList<string> Credits { get; init; } = [];
    public int? SteamAppId { get; init; }
}

/// <summary>IGDB API v4 with a Twitch app token (§5.3 step 2): token cached until expiry, 4 requests/s.</summary>
public sealed class Igdb(MetaHttp http, HttpClient client, string clientId, string clientSecret, TokenBucket? bucket = null, TimeProvider? clock = null)
{
    public const string Attribution = "Oyun bilgileri: IGDB.com";
    const string Images = "https://images.igdb.com/igdb/image/upload/";
    const string Fields = "fields name,first_release_date,summary,genres.name,cover.image_id,artworks.image_id,screenshots.image_id," +
                          "involved_companies.company.name,involved_companies.developer,platforms,external_games.uid,external_games.category,external_games.external_game_source;";
    readonly TokenBucket _bucket = bucket ?? new TokenBucket(4);
    readonly TimeProvider _clock = clock ?? TimeProvider.System;
    string? _token;
    DateTimeOffset _expires;

    async Task<string?> Token(CancellationToken ct)
    {
        if (_token is not null && _clock.GetUtcNow() < _expires) return _token;
        var url = $"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials";
        try
        {
            using var r = await client.PostAsync(url, null, ct);
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            _token = doc.RootElement.GetProperty("access_token").GetString();
            _expires = _clock.GetUtcNow().AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 60);
            return _token;
        }
        catch (HttpRequestException) { return null; }
    }

    async Task<JsonDocument?> Query(string body, CancellationToken ct)
    {
        if (await Token(ct) is not { } token) return null;
        await _bucket.Wait(ct);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games") { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
        req.Headers.TryAddWithoutValidation("Client-ID", clientId);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return await http.Json(req, ct);
    }

    /// <summary>"Bu oyun hangisi?": PC games (platform 6) matching the cleaned name, first three.</summary>
    public async Task<IReadOnlyList<MatchCandidate>> Search(string name, CancellationToken ct)
    {
        var doc = await Query($"search \"{name.Replace("\"", "")}\"; {Fields} where platforms = (6); limit 3;", ct);
        return doc?.RootElement.EnumerateArray().Select(g => new MatchCandidate("igdb", g.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
            Str(g, "name") ?? "", Year(g), Developer(g), CoverId(g) is { } c ? $"{Images}t_cover_small/{c}.jpg" : null)).ToList() ?? [];
    }

    public async Task<(GameMeta Meta, int? SteamAppId)?> Game(string id, CancellationToken ct)
    {
        var doc = await Query($"{Fields} where id = {long.Parse(id, CultureInfo.InvariantCulture)};", ct);
        if (doc is null || doc.RootElement.GetArrayLength() == 0) return null;
        var g = doc.RootElement[0];
        var shots = Ids(g, "screenshots").Take(3).ToList();
        var art = Ids(g, "artworks").FirstOrDefault();
        var steam = g.TryGetProperty("external_games", out var ext)
            ? ext.EnumerateArray().Where(e => Int(e, "category") == 1 || Int(e, "external_game_source") == 1)
                .Select(e => int.TryParse(Str(e, "uid"), out var u) ? u : (int?)null).FirstOrDefault(u => u is not null)
            : null;
        var meta = new GameMeta
        {
            Title = Str(g, "name") ?? "",
            Year = Year(g),
            Summary = Str(g, "summary"),
            Genres = g.TryGetProperty("genres", out var gs) ? gs.EnumerateArray().Select(x => Str(x, "name") ?? "").ToList() : [],
            Developer = Developer(g),
            Cover = await http.Image(CoverId(g) is { } c ? $"{Images}t_cover_big/{c}.jpg" : null, ct),
            Hero = await http.Image(art is null ? null : $"{Images}t_1080p/{art}.jpg", ct),
            Screenshots = (await Task.WhenAll(shots.Select(s => http.Image($"{Images}t_screenshot_big/{s}.jpg", ct)))).OfType<string>().ToList(),
            Credits = [Attribution],
            SteamAppId = steam,
        };
        return (meta, steam);
    }

    static IEnumerable<string> Ids(JsonElement g, string prop) =>
        g.TryGetProperty(prop, out var a) ? a.EnumerateArray().Select(x => Str(x, "image_id")).OfType<string>() : [];
    static string? CoverId(JsonElement g) => g.TryGetProperty("cover", out var c) ? Str(c, "image_id") : null;
    static string? Developer(JsonElement g) =>
        g.TryGetProperty("involved_companies", out var ic)
            ? ic.EnumerateArray().Where(c => c.TryGetProperty("developer", out var d) && d.GetBoolean())
                .Select(c => c.TryGetProperty("company", out var co) ? Str(co, "name") : null).FirstOrDefault(n => n is not null)
            : null;
    static int? Year(JsonElement g) =>
        g.TryGetProperty("first_release_date", out var d) && d.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(d.GetInt64()).Year : null;
    static int? Int(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    static string? Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Steam Store (no key, undocumented): requirements, Turkish description, header and screenshots (§5.3 step 4).</summary>
public sealed partial class SteamStore(MetaHttp http)
{
    public async Task<int?> Find(string name, CancellationToken ct)
    {
        var doc = await http.Json(MetaHttp.Get($"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(name)}&l=turkish&cc=TR"), ct);
        return doc?.RootElement.TryGetProperty("items", out var items) == true
            ? items.EnumerateArray().Where(i => Similar(i.GetProperty("name").GetString() ?? "", name)).Select(i => (int?)i.GetProperty("id").GetInt32()).FirstOrDefault()
            : null;
    }

    public async Task<GameMeta?> Details(int appId, CancellationToken ct)
    {
        var doc = await http.Json(MetaHttp.Get($"https://store.steampowered.com/api/appdetails?appids={appId}&l=turkish"), ct);
        if (doc is null || !doc.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out var app)
            || !app.GetProperty("success").GetBoolean()) return null;
        var d = app.GetProperty("data");
        var shots = d.TryGetProperty("screenshots", out var s) ? s.EnumerateArray().Take(3).Select(x => x.GetProperty("path_full").GetString()).ToList() : [];
        return new GameMeta
        {
            Title = d.GetProperty("name").GetString() ?? "",
            Summary = Text(d.TryGetProperty("short_description", out var sd) ? sd.GetString() : null),
            Genres = d.TryGetProperty("genres", out var g) ? g.EnumerateArray().Select(x => x.GetProperty("description").GetString() ?? "").ToList() : [],
            Developer = d.TryGetProperty("developers", out var dev) && dev.GetArrayLength() > 0 ? dev[0].GetString() : null,
            Hero = await http.Image(d.TryGetProperty("header_image", out var h) ? h.GetString() : null, ct),
            Screenshots = (await Task.WhenAll(shots.Select(u => http.Image(u, ct)))).OfType<string>().ToList(),
            Requirements = d.TryGetProperty("pc_requirements", out var req) && req.ValueKind == JsonValueKind.Object ? Requirements(req) : null,
            SteamAppId = appId,
        };
    }

    /// <summary>Steam's minimum/recommended HTML (Turkish or English labels) → structured fields. Recommended wins.</summary>
    public static Requirements? Requirements(JsonElement pc)
    {
        Dictionary<string, string> Parse(string? html)
        {
            var map = new Dictionary<string, string>();
            if (html is null) return map;
            foreach (Match m in Field().Matches(html))
                map[Key(m.Groups[1].Value)] = Text(m.Groups[2].Value)!;
            return map;
        }
        var min = Parse(pc.TryGetProperty("minimum", out var a) ? a.GetString() : null);
        var rec = Parse(pc.TryGetProperty("recommended", out var b) ? b.GetString() : null);
        string? Pick(string k) => rec.GetValueOrDefault(k) ?? min.GetValueOrDefault(k);
        if (min.Count == 0 && rec.Count == 0) return null;
        return new Requirements
        {
            Os = Pick("os"), Cpu = Pick("cpu"), Gpu = Pick("gpu"), Directx = Pick("directx"),
            RamGb = Gb(Pick("ram")), DiskGb = Gb(Pick("disk")),
        };
    }

    // "İşletim".ToLowerInvariant() is "i̇şletim" (i + combining dot): fold the Turkish capital first.
    static string Key(string label) => label.Trim().TrimEnd(':').Replace('İ', 'i').ToLowerInvariant() switch
    {
        "işletim sistemi" or "os" or "os *" => "os",
        "işlemci" or "processor" => "cpu",
        "bellek" or "memory" => "ram",
        "ekran kartı" or "graphics" => "gpu",
        "directx" => "directx",
        "depolama" or "storage" or "hard drive" or "disk alanı" => "disk",
        var other => other,
    };

    /// <summary>"8 GB RAM" → 8, "512 MB RAM" → 0.5, "70 GB kullanılabilir alan" → 70.</summary>
    public static double? Gb(string? s)
    {
        if (s is null) return null;
        var m = Regex.Match(s, @"(\d+(?:[.,]\d+)?)\s*(GB|MB|TB)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToUpperInvariant() switch { "MB" => Math.Round(v / 1024, 2), "TB" => v * 1024, _ => v };
    }

    [GeneratedRegex(@"<strong>([^<]+)</strong>\s*(.*?)(?:<br\s*/?>|</li>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Field();

    static string? Text(string? html) =>
        html is null ? null : WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")).Replace("  ", " ").Trim() is { Length: > 0 } t ? t : null;

    static bool Similar(string a, string b) =>
        Normalize(a) == Normalize(b) || Normalize(a).StartsWith(Normalize(b), StringComparison.Ordinal);
    static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
}

/// <summary>SteamGridDB (optional key): transparent logo and hero (§5.3 step 5).</summary>
public sealed class SteamGridDb(MetaHttp http, string key)
{
    public const string Attribution = "Görseller: SteamGridDB";
    (string, string) Auth => ("Authorization", $"Bearer {key}");

    public async Task<(string? Logo, string? Hero)> Art(string name, int? steamAppId, CancellationToken ct)
    {
        var id = steamAppId is { } app
            ? Id(await http.Json(MetaHttp.Get($"https://www.steamgriddb.com/api/v2/games/steam/{app}", Auth), ct), single: true)
            : Id(await http.Json(MetaHttp.Get($"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(name)}", Auth), ct), single: false);
        if (id is null) return (null, null);
        return (await http.Image(Best(await http.Json(MetaHttp.Get($"https://www.steamgriddb.com/api/v2/logos/game/{id}", Auth), ct)), ct),
                await http.Image(Best(await http.Json(MetaHttp.Get($"https://www.steamgriddb.com/api/v2/heroes/game/{id}", Auth), ct)), ct));
    }

    static string? Id(JsonDocument? doc, bool single)
    {
        if (doc?.RootElement.TryGetProperty("data", out var d) != true) return null;
        var game = single ? d : d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0 ? d[0] : default;
        return game.ValueKind == JsonValueKind.Object ? game.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture) : null;
    }

    static string? Best(JsonDocument? doc) =>
        doc?.RootElement.TryGetProperty("data", out var d) == true && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().OrderByDescending(x => x.TryGetProperty("score", out var s) ? s.GetInt32() : 0).Select(x => x.GetProperty("url").GetString()).FirstOrDefault()
            : null;
}

/// <summary>§5.3 game chain: IGDB → Steam (via IGDB's Steam id or a store search) → SteamGridDB, merged with the hero
/// priority SteamGridDB hero &gt; IGDB artwork &gt; Steam header &gt; first screenshot.</summary>
public static class GameLookup
{
    public static async Task<GameMeta?> Resolve(MatchCandidate? igdbMatch, string name, Igdb? igdb, SteamStore steam, SteamGridDb? grid, CancellationToken ct)
    {
        (GameMeta Meta, int? SteamAppId)? fromIgdb = igdbMatch is not null && igdb is not null ? await igdb.Game(igdbMatch.Id, ct) : null;
        var appId = fromIgdb?.SteamAppId ?? await steam.Find(fromIgdb?.Meta.Title ?? name, ct);
        var fromSteam = appId is { } id ? await steam.Details(id, ct) : null;
        if (fromIgdb is null && fromSteam is null) return null;
        var art = grid is not null ? await grid.Art(fromIgdb?.Meta.Title ?? fromSteam!.Title, appId, ct) : (null, null);
        var i = fromIgdb?.Meta;
        var s = fromSteam;
        var screenshots = i?.Screenshots.Count > 0 ? i.Screenshots : s?.Screenshots ?? [];
        return new GameMeta
        {
            Title = i?.Title ?? s!.Title,
            Year = i?.Year,
            Summary = s?.Summary ?? i?.Summary, // Steam's short description is Turkish (l=turkish)
            Genres = s?.Genres.Count > 0 ? s.Genres : i?.Genres ?? [],
            Developer = i?.Developer ?? s?.Developer,
            Cover = i?.Cover,
            Hero = art.Hero ?? i?.Hero ?? s?.Hero ?? screenshots.FirstOrDefault(),
            Logo = art.Logo,
            Screenshots = screenshots,
            Requirements = s?.Requirements,
            SteamAppId = appId,
            Credits = [.. i?.Credits ?? [], .. s is not null ? ["Steam"] : Array.Empty<string>(), .. (art.Logo ?? art.Hero) is not null ? [SteamGridDb.Attribution] : Array.Empty<string>()],
        };
    }
}
