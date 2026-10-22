using System.Text.Json;
using Inlay.Core;
using Json.Schema;

namespace Inlay.Launcher;

public sealed record LoadedDisc(DiscManifest? Manifest, string? Json, string? Error);

/// <summary>Reads .disc/manifest.json and checks it against the embedded schema before anything trusts it (§9.3.3).</summary>
public static class DiscLoader
{
    public const string ManifestPath = ".disc/manifest.json";

    static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var s = typeof(DiscLoader).Assembly.GetManifestResourceStream("manifest.schema.json")!;
        return JsonSchema.FromText(new StreamReader(s).ReadToEnd());
    });

    public static LoadedDisc Load(string root)
    {
        var path = RelPath.Resolve(root, ManifestPath);
        if (path is null || !File.Exists(path)) return new(null, null, "missing");
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (!Schema.Value.Evaluate(doc.RootElement).IsValid) return new(null, null, "schema");
            return new(JsonSerializer.Deserialize(json, InlayJson.Default.DiscManifest), json, null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(null, null, e.GetType().Name);
        }
    }
}
