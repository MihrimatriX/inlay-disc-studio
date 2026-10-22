using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inlay.Analysis.Online;

/// <summary>Builder-wide paths under %LOCALAPPDATA%\Inlay (§5.3 cache, §6.2 work).</summary>
public static class InlayFolders
{
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inlay");
    public static string MetaCache => Path.Combine(Root, "cache", "meta");
    public static string ImageCache => Path.Combine(Root, "cache", "images");
    public static string Work(string projectId) => Path.Combine(Root, "work", projectId);
}

/// <summary>GET/POST with a 30-day response cache (§5.3). Network failures and HTTP 429 return null: metadata is
/// a nicety, the disc must still be buildable offline.</summary>
public sealed class MetaHttp(HttpClient client, TimeSpan? maxAge = null)
{
    readonly TimeSpan _maxAge = maxAge ?? TimeSpan.FromDays(30);

    public async Task<JsonDocument?> Json(HttpRequestMessage request, CancellationToken ct, bool cache = true)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.Method} {request.RequestUri} {body}")));
        var file = Path.Combine(InlayFolders.MetaCache, key + ".json");
        if (cache && File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < _maxAge)
            return JsonDocument.Parse(await File.ReadAllTextAsync(file, ct));
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null; // 401/404/429…: skip quietly (§5.3 Steam note)
            var text = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(text);
            if (cache)
            {
                Directory.CreateDirectory(InlayFolders.MetaCache);
                await File.WriteAllTextAsync(file, text, ct);
            }
            return doc;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Downloads an image once into the image cache and returns the local path (embedded on the disc later).</summary>
    public async Task<string?> Image(string? url, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var ext = Path.GetExtension(new Uri(url).AbsolutePath) is { Length: > 1 and < 6 } e ? e : ".jpg";
        var file = Path.Combine(InlayFolders.ImageCache, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32] + ext);
        if (File.Exists(file)) return file;
        try
        {
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            Directory.CreateDirectory(InlayFolders.ImageCache);
            var tmp = file + ".part";
            await using (var fs = File.Create(tmp)) await response.Content.CopyToAsync(fs, ct);
            File.Move(tmp, file, overwrite: true);
            return file;
        }
        catch (HttpRequestException) { return null; }
    }

    public static HttpRequestMessage Get(string url, params (string Name, string Value)[] headers)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (n, v) in headers) r.Headers.TryAddWithoutValidation(n, v);
        return r;
    }
}

/// <summary>Allows <paramref name="perSecond"/> requests per second (IGDB: 4).</summary>
public sealed class TokenBucket(int perSecond, TimeProvider? clock = null)
{
    readonly TimeProvider _clock = clock ?? TimeProvider.System;
    readonly SemaphoreSlim _gate = new(1);
    readonly Queue<long> _recent = new();

    public async Task Wait(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = _clock.GetTimestamp();
                while (_recent.Count > 0 && _clock.GetElapsedTime(_recent.Peek(), now) >= TimeSpan.FromSeconds(1)) _recent.Dequeue();
                if (_recent.Count < perSecond) { _recent.Enqueue(now); return; }
                await Task.Delay(TimeSpan.FromSeconds(1) - _clock.GetElapsedTime(_recent.Peek(), now), _clock, ct);
            }
        }
        finally { _gate.Release(); }
    }
}

/// <summary>API keys (TMDB, Twitch client id/secret, SteamGridDB) encrypted with DPAPI for the current user (§5.3, §10).</summary>
public sealed class KeyStore(string? path = null)
{
    readonly string _path = path ?? Path.Combine(InlayFolders.Root, "keys.dat");
    static readonly byte[] Entropy = "Inlay.KeyStore.v1"u8.ToArray();

    public string? Get(string name) => Load().GetValueOrDefault(name);

    public void Set(string name, string? value)
    {
        var all = Load();
        if (string.IsNullOrEmpty(value)) all.Remove(name); else all[name] = value;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(all);
        File.WriteAllBytes(_path, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
    }

    Dictionary<string, string> Load()
    {
        if (!File.Exists(_path)) return [];
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? [];
    }
}

public sealed record MatchCandidate(string Provider, string Id, string Title, int? Year, string? Detail, string? ThumbUrl);
