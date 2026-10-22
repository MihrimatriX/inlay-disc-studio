using System.Text.Json;
using System.Text.Json.Serialization;
using Inlay.Analysis;
using Inlay.Core;

namespace Inlay.Builder;

/// <summary>The UI ↔ host protocol: the page sends <c>{id, method, params}</c>, the host answers
/// <c>{id, ok, result | error}</c> and pushes <c>{event, data}</c>. Window and dialog methods are the host's; everything
/// else is a <see cref="Session"/> call, mapped here so it can be exercised without a window.</summary>
public static class Rpc
{
    /// <summary>Goes to PostWebMessageAsJson, never into HTML, so non-ASCII text can stay as it is.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(InlayJson.Default, new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()),
    };

    /// <summary>Runs one session method. Unknown methods throw <see cref="ArgumentException"/>.</summary>
    public static async Task<object?> Call(Session s, string method, JsonElement p)
    {
        string Str(string name) => p.GetProperty(name).GetString() ?? throw new ArgumentException(name);
        string? Opt(string name) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        T Obj<T>(string? name = null) => (name is null ? p : p.GetProperty(name)).Deserialize<T>(Json) ?? throw new ArgumentException(name ?? method);

        switch (method)
        {
            case "state": return s.View();
            case "project.new": s.New(); return null;
            case "project.open": await s.Open(Str("path")); return null;
            case "project.save": s.Save(Opt("path")); return null;
            case "project.relink": await s.Relink(Str("old"), Str("now")); return null;
            case "undo": await s.Undo(); return null;
            case "redo": await s.Redo(); return null;
            case "content.add": await s.Add(Obj<string[]>("paths")); return null;
            case "content.remove": await s.Remove(Obj<string[]>("ids")); return null;
            case "content.kind": await s.SetKind(Str("id"), Enum.Parse<ContentKind>(Str("kind"), ignoreCase: true)); return null;
            case "content.match": await s.Match(Str("id"), Obj<MatchChoice>("choice")); return null;
            case "content.rename": await s.Rename(Str("id"), Str("title")); return null;
            case "theme.image": await s.SetImage(Str("key"), Str("path")); return null;
            case "content.suggest": await s.Apply(p.GetProperty("index").GetInt32()); return null;
            case "menu.template": await s.Template(Str("id")); return null;
            case "menu.set": await s.SetMenu(Obj<MenuEdit>()); return null;
            case "theme.setting": await s.SetSetting(Str("key"), p.GetProperty("value")); return null;
            case "theme.sections": await s.SetSections(Obj<SectionEdit[]>("sections")); return null;
            case "theme.copy": await s.CopyTheme(); return null;
            case "theme.read": return s.ReadThemeFile(Str("path"));
            case "theme.write": await s.WriteThemeFile(Str("path"), Str("text")); return null;
            case "theme.import": await s.ImportTheme(Str("path")); return null;
            case "theme.export": s.ExportTheme(Str("path")); return null;
            case "editor.data": return s.Data();
            case "prefs.set": s.SetPrefs(Obj<PrefsEdit>()); return null;
            case "keys.set": await s.SetKey(Str("name"), Opt("value")); return null;
            case "drives.refresh": await s.RefreshDrives(); return null;
            default: throw new ArgumentException($"unknown method {method}");
        }
    }
}
