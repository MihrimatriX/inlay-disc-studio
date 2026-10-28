using System.Collections.Concurrent;
using System.Text.Json;

namespace Inlay.Core;

/// <summary>User-facing strings from i18n/*.json. tr-TR is the default and the fallback; en-US is the second language.</summary>
public static class Text
{
    public const string DefaultLocale = "tr-TR";
    static readonly ConcurrentDictionary<string, Dictionary<string, string>> Tables = new();

    public static string Get(string key, string? locale = null)
    {
        var loc = Resolve(locale);
        if (Table(loc).TryGetValue(key, out var s)) return s;
        if (loc != DefaultLocale && Table(DefaultLocale).TryGetValue(key, out s)) return s;
        throw new KeyNotFoundException($"Missing text '{key}'");
    }

    public static string Resolve(string? locale) =>
        locale?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en-US" : DefaultLocale;

    public static IReadOnlyDictionary<string, string> Table(string locale) => Tables.GetOrAdd(locale, static loc =>
    {
        using var s = typeof(Text).Assembly.GetManifestResourceStream($"i18n.{loc}.json")
            ?? throw new InvalidOperationException($"No strings for {loc}");
        return JsonSerializer.Deserialize(s, InlayJson.Default.DictionaryStringString)!;
    });
}
