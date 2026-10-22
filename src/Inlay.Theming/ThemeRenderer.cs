using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fluid;
using Fluid.Values;
using Inlay.Core;

namespace Inlay.Theming;

/// <summary>One rendered disc UI: page HTML and the package files that go with it, all under .disc/ui/ (§8.1).</summary>
public sealed record RenderedUi(string IndexHtml, string FallbackHtml, IReadOnlyDictionary<string, byte[]> Assets, IReadOnlyList<LintIssue> Issues)
{
    public bool Blocking => Issues.Any(i => i.Severity == LintSeverity.Error);
}

/// <summary>§7.1 render pipeline: manifest + theme + settings → Fluid → index.html with CSS variables and the CSP meta tag.
/// The same output feeds the launcher, the builder preview (§7.3) and, rendered unhosted, /Menu.html (§8.1).</summary>
public static partial class ThemeRenderer
{
    public const string UiRoot = ".disc/ui/";
    static readonly FluidParser Parser = new(new FluidParserOptions { AllowFunctions = false });

    public static RenderedUi Render(DiscManifest manifest, ThemeFiles theme, string? sdkScript = null)
    {
        var issues = new List<LintIssue>();
        var settings = Settings.Resolve(theme.Theme, manifest);
        string index, fallback;
        if (theme.Theme.Engine == ThemeEngine.Static)
        {
            index = theme.Text(theme.Theme.Entry);
            fallback = index;
        }
        else
        {
            if (!Parser.TryParse(theme.Text(theme.Theme.Entry), out var template, out var error))
            {
                issues.Add(new LintIssue(LintSeverity.Error, "liquid-syntax", error ?? "syntax error"));
                return new RenderedUi("", "", new Dictionary<string, byte[]>(), issues);
            }
            index = Run(template, manifest, theme, settings, hosted: true, issues);
            fallback = Run(template, manifest, theme, settings, hosted: false, issues);
        }

        index = Inject(index, manifest, settings, null);
        fallback = Inject(fallback, manifest, settings, sdkScript is null ? null : Boot(manifest) + sdkScript);
        // Menu.html sits at the disc root: package files (assets/…) move under .disc/ui/; "./…" from relurl is already root-relative.
        fallback = Regex.Replace(fallback, "(src|href)=\"(?!https?:|data:|blob:|#|/|\\./)([^\"]+)\"", m => $"{m.Groups[1].Value}=\"./{UiRoot}{m.Groups[2].Value}\"");

        var assets = theme.Files.Where(f => f.Key.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) || f.Key.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.Key, f => f.Value);
        issues.AddRange(Lint.Check(index, manifest, theme, assets, settings));
        return new RenderedUi(index, fallback, assets, issues);
    }

    static string Run(IFluidTemplate template, DiscManifest manifest, ThemeFiles theme, IReadOnlyDictionary<string, object?> settings, bool hosted, List<LintIssue> issues)
    {
        var options = new TemplateOptions
        {
            FileProvider = theme.Provider,
            MaxSteps = 2_000_000,
            MaxRecursion = 32,
            CultureInfo = CultureInfo.GetCultureInfo(Text.Resolve(manifest.Disc.Locale)),
        };
        options.MemberAccessStrategy = new DefaultMemberAccessStrategy(); // nothing registered: templates see data only
        Filters.Register(options, manifest, hosted);
        var context = new TemplateContext(Model.Of(manifest, theme.Theme, settings, hosted), options);
        try
        {
            return template.Render(context, HtmlEncoder.Default);
        }
        catch (Exception e) when (e is InvalidOperationException or ParseException or FluidException)
        {
            issues.Add(new LintIssue(LintSeverity.Error, "liquid-runtime", e.Message));
            return "";
        }
    }

    /// <summary>The SDK boot object for the unhosted Menu.html (the launcher injects it itself when hosted).</summary>
    public static string Boot(DiscManifest m) =>
        $"window.__inlayBoot={{\"manifest\":{JsonSerializer.Serialize(m, InlayJson.Default.DiscManifest).Replace("</", "<\\/")},\"hosted\":false}};\n";

    /// <summary>CSP meta (§10) first in head, CSS variables after the theme's own styles (§7.1.4).</summary>
    static string Inject(string html, DiscManifest manifest, IReadOnlyDictionary<string, object?> settings, string? script)
    {
        var vars = new StringBuilder(":root{");
        foreach (var (k, v) in Palette(manifest.Theme.Palette)) vars.Append($"--ds-{k}:{v};");
        foreach (var (k, v) in settings)
            if (v is string or double or int or long) vars.Append($"--ds-{Kebab(k)}:{CssValue(v)};");
        vars.Append('}');
        var head = $"<meta http-equiv=\"Content-Security-Policy\" content=\"{DiscSecurity.ContentSecurityPolicy}\">";
        var tail = $"<style id=\"ds-vars\">{vars}</style>" + (script is null ? "" : $"<script>{script}</script>");
        html = HeadOpen().IsMatch(html) ? HeadOpen().Replace(html, m => m.Value + head, 1) : head + html;
        return html.Contains("</head>", StringComparison.OrdinalIgnoreCase)
            ? Regex.Replace(html, "</head>", tail + "</head>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            : tail + html;
    }

    internal static IEnumerable<(string, string)> Palette(Palette p) =>
        [("bg", p.Bg), ("surface", p.Surface), ("accent", p.Accent), ("accent-text", p.AccentText ?? p.Bg), ("text", p.Text), ("muted", p.Muted)];

    static string CssValue(object v) => v switch
    {
        string s when Regex.IsMatch(s, "^#[0-9a-fA-F]{6,8}$") => s,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _) => s, // select values like "4" stay numbers for calc()/repeat()
        string s => JsonSerializer.Serialize(s, InlayJson.Default.String),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => "initial",
    };

    static string Kebab(string s) => Regex.Replace(s, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();

    [GeneratedRegex("<head[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadOpen();
}

/// <summary>theme.json settings → resolved values: user choice from the manifest, else the default; "auto" colors come
/// from the palette (§4.2).</summary>
public static class Settings
{
    public static IReadOnlyDictionary<string, object?> Resolve(ThemePackage theme, DiscManifest manifest)
    {
        var chosen = manifest.Theme.Settings ?? new Dictionary<string, JsonElement>();
        var palette = ThemeRenderer.Palette(manifest.Theme.Palette).ToDictionary(p => p.Item1, p => p.Item2);
        var result = new Dictionary<string, object?>();
        foreach (var (key, def) in theme.Settings ?? new Dictionary<string, ThemeSetting>())
        {
            var value = chosen.TryGetValue(key, out var v) ? Plain(v) : def.Default is { } d ? Plain(d) : null;
            if (def.Type == SettingType.Color && value is string s && s.StartsWith("auto", StringComparison.Ordinal))
                value = palette[s == "auto" ? (palette.ContainsKey(key) ? key : "accent") : s[5..]]; // "auto:muted" → palette.muted
            result[key] = value;
        }
        return result;
    }

    static object? Plain(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}

/// <summary>The Liquid model (§7.1.2) as plain dictionaries and lists, so templates can read data and nothing else.</summary>
static class Model
{
    public static Dictionary<string, object?> Of(DiscManifest m, ThemePackage theme, IReadOnlyDictionary<string, object?> settings, bool hosted)
    {
        var root = Plain(JsonSerializer.SerializeToElement(m, InlayJson.Default.DiscManifest)) as Dictionary<string, object?> ?? [];
        var items = (List<object?>)root["items"]!;
        var actions = (List<object?>)root["actions"]!;
        var byId = items.Cast<Dictionary<string, object?>>().ToDictionary(i => (string)i["id"]!, i => (object?)i);
        foreach (var s in ((List<object?>)root["sections"]!).Cast<Dictionary<string, object?>>())
            s["entries"] = ((List<object?>)s["items"]!).Select(id => byId.GetValueOrDefault((string)id!)).Where(x => x is not null).ToList();
        return new Dictionary<string, object?>
        {
            ["disc"] = root["disc"],
            ["sections"] = root["sections"],
            ["items"] = byId,
            ["all_items"] = items,
            ["actions"] = actions.Cast<Dictionary<string, object?>>().ToDictionary(a => (string)a["id"]!, a => (object?)a),
            ["all_actions"] = actions,
            ["credits"] = root.GetValueOrDefault("credits"),
            ["licenses"] = root.GetValueOrDefault("licenses"),
            ["theme"] = new Dictionary<string, object?>
            {
                ["package"] = theme.Id, ["name"] = theme.Name, ["version"] = theme.Version,
                ["palette"] = (root["theme"] as Dictionary<string, object?>)?["palette"],
            },
            ["settings"] = settings.ToDictionary(kv => kv.Key, kv => kv.Value),
            ["hosted"] = hosted,
            // The builder's and the installer's strings share the tables but never go on a disc.
            ["text"] = Text.Table(Text.Resolve(m.Disc.Locale)).Where(kv => !kv.Key.StartsWith("builder.", StringComparison.Ordinal) && !kv.Key.StartsWith("setup.", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
        };
    }

    static object? Plain(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => Plain(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(Plain).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}

/// <summary>§7.1.2 filters: duration, clock, filesize, relurl, t, json.</summary>
public static class Filters
{
    public static void Register(TemplateOptions o, DiscManifest m, bool hosted)
    {
        var locale = Text.Resolve(m.Disc.Locale);
        var culture = CultureInfo.GetCultureInfo(locale);
        o.Filters.AddFilter("duration", (input, _, _) => new StringValue(Duration(input.ToNumberValue(), locale)));
        o.Filters.AddFilter("clock", (input, _, _) => new StringValue(Clock(input.ToNumberValue())));
        o.Filters.AddFilter("filesize", (input, _, _) => new StringValue(FileSize(input.ToNumberValue(), culture)));
        o.Filters.AddFilter("relurl", (input, _, _) => new StringValue(RelUrl(input.ToStringValue(), hosted)));
        o.Filters.AddFilter("t", (input, args, _) =>
        {
            var key = input.ToStringValue();
            string s;
            try { s = Text.Get(key, locale); }
            catch (KeyNotFoundException) { s = key; }
            for (var i = 0; i < args.Count; i++) s = s.Replace("{" + i + "}", args.At(i).ToStringValue(), StringComparison.Ordinal);
            return new StringValue(s);
        });
        o.Filters.AddFilter("json", (input, _, _) => new StringValue(Json(Plain(input)), encode: false));
    }

    /// <summary>7322 → "2 sa 2 dk"; 1440 → "24 dk"; 45 → "1 dk".</summary>
    public static string Duration(decimal sec, string locale = Text.DefaultLocale)
    {
        var total = (int)Math.Round((double)sec / 60);
        var (h, m) = (total / 60, total % 60);
        var tr = locale.StartsWith("tr", StringComparison.Ordinal);
        if (h == 0) return tr ? $"{Math.Max(1, m)} dk" : $"{Math.Max(1, m)} min";
        return tr ? (m == 0 ? $"{h} sa" : $"{h} sa {m} dk") : (m == 0 ? $"{h} h" : $"{h} h {m} min");
    }

    /// <summary>201 → "3:21"; 3725 → "1:02:05".</summary>
    public static string Clock(decimal sec)
    {
        var t = TimeSpan.FromSeconds((double)Math.Round(sec));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>4_520_000_000 → "4,21 GB" (binary units, decimal comma for tr-TR).</summary>
    public static string FileSize(decimal bytes, CultureInfo culture)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var v = (double)bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{v:0} B" : (v >= 100 ? v.ToString("0", culture) : v.ToString("0.##", culture)) + " " + units[u];
    }

    public static string RelUrl(string path, bool hosted)
    {
        var encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return hosted ? "https://disc.local/" + encoded : "./" + encoded;
    }

    /// <summary>Fluid wraps dictionaries and arrays; unwrap them so json sees the data, not the wrapper.</summary>
    static object? Plain(object? v) => v switch
    {
        null or NilValue => null,
        BooleanValue b => b.ToBooleanValue(),
        NumberValue n => n.ToNumberValue(),
        StringValue s => s.ToStringValue(),
        ArrayValue a => a.Values.Select(Plain).ToList(),
        FluidValue f => Plain(f.ToObjectValue()),
        Fluid.Values.IFluidIndexable d => d.Keys.ToDictionary(k => k, k => d.TryGetValue(k, out var x) ? Plain(x) : null),
        IDictionary<string, object?> d => d.ToDictionary(kv => kv.Key, kv => Plain(kv.Value)),
        string s => s,
        System.Collections.IEnumerable e => e.Cast<object?>().Select(Plain).ToList(),
        _ => v,
    };

    /// <summary>Safe inside &lt;script&gt;: no "&lt;/script", no HTML comment openers, no line separators.</summary>
    public static string Json(object? value)
    {
        var s = JsonSerializer.Serialize(value, JsonOptions);
        return s.Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026").Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
    }

    static readonly JsonSerializerOptions JsonOptions = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
}
