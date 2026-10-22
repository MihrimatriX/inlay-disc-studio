using System.Text.Json;
using System.Text.Json.Serialization;
using Inlay.Analysis;
using Inlay.Core;
using Inlay.Media;

namespace Inlay.Builder;

/// <summary>§7.4 <c>.discproj</c>: what the user chose, never what analysis can recompute. Source paths are absolute;
/// corrections and matches are keyed by the scanner's relative paths, so they survive "kaynağı yeniden bağla".</summary>
public sealed class DiscProject
{
    public int Version { get; set; } = 1;
    /// <summary>Names the work folder (%LOCALAPPDATA%\Inlay\work\&lt;id&gt;) so moving the project keeps its prepared files.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    public List<string> Sources { get; set; } = [];
    /// <summary>Scanner-relative paths (file or folder) the user removed from the disc.</summary>
    public List<string> Excluded { get; set; } = [];
    /// <summary>§5.2 corrections: scanner-relative path (file or folder) → kind.</summary>
    public Dictionary<string, ContentKind> Kinds { get; set; } = [];
    /// <summary>"Bu film hangisi?" answers, by the group's first main file.</summary>
    public Dictionary<string, MatchChoice> Matches { get; set; } = [];
    public bool KeepOriginals { get; set; }
    public bool MaxCompatibility { get; set; }
    public VideoEncoder Encoder { get; set; }
    /// <summary>Standard theme package id; null picks the one for the content (§7.2). Ignored while <see cref="UserTheme"/> is set.</summary>
    public string? Theme { get; set; }
    public Dictionary<string, JsonElement> Settings { get; set; } = [];
    /// <summary>The copied-and-edited theme, embedded: package path → base64 content.</summary>
    public Dictionary<string, string>? UserTheme { get; set; }
    /// <summary>F2 in "Disk içeriği": item titles the user changed, by the group's first main file.</summary>
    public Dictionary<string, string> Renames { get; set; } = [];
    /// <summary>Extra disc files the user chose (image settings): disc path → absolute source.</summary>
    public Dictionary<string, string> Files { get; set; } = [];
    /// <summary>Section editor: order, titles, layouts, hidden flags. Null keeps the composed sections.</summary>
    public List<SectionEdit>? Sections { get; set; }
    public bool Autoplay { get; set; }
    public bool Remote { get; set; } = true;
    public string? Title { get; set; }
    public string? Label { get; set; }
    public DiscMedia? Media { get; set; }
    /// <summary>Picture chosen with "Değiştir" for the disc icon; null derives it from the poster/cover.</summary>
    public string? Icon { get; set; }
    public string Locale { get; set; } = Text.DefaultLocale;

    public const string Extension = ".discproj";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static DiscProject Load(string path) =>
        JsonSerializer.Deserialize<DiscProject>(File.ReadAllText(path), Json) ?? throw new InvalidDataException(path);

    /// <summary>Written next to itself first: a crash mid-save never leaves a half project.</summary>
    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public DiscProject Clone() => JsonSerializer.Deserialize<DiscProject>(JsonSerializer.Serialize(this, Json), Json)!;

    /// <summary>Sources that are gone (moved disk, renamed folder): the UI offers to relink them.</summary>
    public IEnumerable<string> Missing => Sources.Where(s => !Directory.Exists(s) && !File.Exists(s));
}

/// <summary>A provider match ("tmdb:movie", "tmdb:tv", "igdb") or, without one, what the user typed.</summary>
public sealed record MatchChoice(string? Provider, string? Id, string? Title = null, int? Year = null, string? Poster = null);

public sealed record SectionEdit(string Id, string? Title, SectionLayout? Layout, bool Hidden);
