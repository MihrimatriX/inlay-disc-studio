using System.Text.Json;
using System.Text.Json.Serialization;
using Inlay.Analysis.Online;
using Inlay.Core;
using Inlay.Media;

namespace Inlay.Builder;

/// <summary>Builder preferences (Ayarlar): accent colour, recent projects and the "Gelişmiş" defaults new projects
/// start from (§11). API keys live in the DPAPI <see cref="KeyStore"/>, never here.</summary>
public sealed class Prefs
{
    public string Accent { get; set; } = "orange";
    public string Locale { get; set; } = Text.DefaultLocale;
    public List<string> Recent { get; set; } = [];
    public bool KeepOriginals { get; set; }
    public bool MaxCompatibility { get; set; }
    public VideoEncoder Encoder { get; set; }
    public int UdfRevision { get; set; } = 0x201;
    /// <summary>Write speed as IMAPI2 sectors/s; 0 is the recorder's best.</summary>
    public int Speed { get; set; }
    public bool Verify { get; set; } = true;
    public string? Recorder { get; set; }

    [JsonIgnore] public string File { get; private set; } = Path.Combine(InlayFolders.Root, "prefs.json");

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Prefs Load(string? file = null)
    {
        file ??= Path.Combine(InlayFolders.Root, "prefs.json");
        Prefs p;
        try { p = System.IO.File.Exists(file) ? JsonSerializer.Deserialize<Prefs>(System.IO.File.ReadAllText(file), Json) ?? new() : new(); }
        catch (JsonException) { p = new(); } // a damaged prefs file is replaced, it holds nothing that cannot be chosen again
        p.File = file;
        return p;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, Json));
    }

    public void Opened(string project)
    {
        Recent.RemoveAll(r => string.Equals(r, project, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, project);
        if (Recent.Count > 10) Recent.RemoveRange(10, Recent.Count - 10);
        Save();
    }
}
