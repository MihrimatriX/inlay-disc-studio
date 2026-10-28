using System.Text.Json;
using System.Text.Json.Serialization;
using Inlay.Core;

namespace Inlay.Launcher;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RunResult))]
[JsonSerializable(typeof(BundleResult))]
[JsonSerializable(typeof(BundleProgress))]
[JsonSerializable(typeof(ActionEvent))]
[JsonSerializable(typeof(SystemInfo))]
[JsonSerializable(typeof(FocusEvent))]
public sealed partial class LauncherJson : JsonSerializerContext;

public sealed record FocusEvent(bool Focused);

/// <summary>Page ↔ launcher protocol (sdk/disc-sdk.d.ts): {v, req, cmd, id, opts} → {v, res, ok, data | error}.</summary>
public sealed class Bridge(ActionRunner actions, BundleRunner bundles, DiscManifest manifest, IBridgeHost host)
{
    public async Task<string?> Handle(string message)
    {
        int req;
        string? cmd, id;
        JsonElement opts;
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1)
                return null;
            req = root.GetProperty("req").GetInt32();
            cmd = root.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            opts = root.TryGetProperty("opts", out var o) && o.ValueKind == JsonValueKind.Object ? o.Clone() : default;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null; // not a protocol message; nothing to answer
        }

        try
        {
            return Ok(req, await Dispatch(cmd, id, opts));
        }
        catch (BridgeError e) { return Fail(req, e.Code); }
        catch (Exception) { return Fail(req, "internal"); }
    }

    async Task<string?> Dispatch(string? cmd, string? id, JsonElement opts)
    {
        switch (cmd)
        {
            case "run":
                return Json(await actions.Run(id, bundles.Run), LauncherJson.Default.RunResult);
            case "runInstalled":
                return Json(actions.RunInstalled(id), LauncherJson.Default.RunResult);
            case "installBundle":
                var ids = opts.ValueKind == JsonValueKind.Object && opts.TryGetProperty("appIds", out var a) && a.ValueKind == JsonValueKind.Array
                    ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                    : throw new BridgeError("unknown-action");
                return Json(await bundles.Run(BundlePlanner.Plan(manifest, ids)), LauncherJson.Default.BundleResult);
            case "system":
                return Json(host.Probe(), LauncherJson.Default.SystemInfo);
            case "setFullscreen":
                host.SetFullscreen(opts.ValueKind == JsonValueKind.Object && opts.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True);
                return null;
            case "close":
                host.Close();
                return null;
            case "play" or "playlist" or "slideshow" or "player.pause" or "player.resume" or "player.stop"
                or "player.seek" or "player.next" or "player.prev" or "player.setVolume":
                return await host.Player(cmd, id, opts);
            default:
                throw new BridgeError("unknown-action");
        }
    }

    static string Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);

    static string Ok(int req, string? data) => $$"""{"v":1,"res":{{req}},"ok":true{{(data is null ? "" : ",\"data\":" + data)}}}""";
    static string Fail(int req, string code) => $$"""{"v":1,"res":{{req}},"ok":false,"error":{{JsonSerializer.Serialize(code, InlayJson.Default.String)}}}""";

    public static string Event<T>(string name, T data, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        $$"""{"v":1,"event":{{JsonSerializer.Serialize(name, InlayJson.Default.String)}},"data":{{JsonSerializer.Serialize(data, info)}}}""";
}

/// <summary>What the bridge needs from the window it lives in.</summary>
public interface IBridgeHost
{
    SystemInfo Probe();
    void SetFullscreen(bool on);
    void Close();
    /// <summary>Media commands for libvlc/shell engines; returns PlayerState JSON or throws BridgeError.</summary>
    Task<string?> Player(string cmd, string? id, JsonElement opts);
}
