using System.Text.RegularExpressions;
using Inlay.Core;

namespace Inlay.Theming;

public enum LintSeverity { Warning, Error }

/// <summary>A finding shown in the editor's lint panel (§7.3). Errors block burning (§7.1.5).</summary>
public sealed record LintIssue(LintSeverity Severity, string Rule, string Message);

/// <summary>§7.1.5 rules, run on the rendered page.</summary>
public static partial class Lint
{
    static readonly Dictionary<string, ThemeCapability> SdkCalls = new()
    {
        ["play"] = ThemeCapability.Play, ["playlist"] = ThemeCapability.Playlist, ["slideshow"] = ThemeCapability.Slideshow,
        ["run"] = ThemeCapability.Run, ["installBundle"] = ThemeCapability.Install, ["setFullscreen"] = ThemeCapability.Fullscreen,
    };

    public static IReadOnlyList<LintIssue> Check(string html, DiscManifest m, ThemeFiles theme, IReadOnlyDictionary<string, byte[]> assets, IReadOnlyDictionary<string, object?> settings)
    {
        var issues = new List<LintIssue>();
        var actions = m.Actions.Select(a => a.Id).ToHashSet();
        var scripts = string.Join('\n', new[] { html }.Concat(assets.Where(a => a.Key.EndsWith(".js", StringComparison.OrdinalIgnoreCase)).Select(a => System.Text.Encoding.UTF8.GetString(a.Value))));

        foreach (Match x in RunCall().Matches(scripts).Concat(DataAction().Matches(html)))
            if (!actions.Contains(x.Groups[1].Value))
                issues.Add(new(LintSeverity.Error, "unknown-action", $"\"{x.Groups[1].Value}\" is not in manifest.actions"));

        foreach (Match x in ExternalUrl().Matches(html))
            if (!x.Groups[2].Value.StartsWith("https://disc.local/", StringComparison.OrdinalIgnoreCase))
                issues.Add(new(LintSeverity.Error, x.Groups[1].Value.Equals("src", StringComparison.OrdinalIgnoreCase) ? "external-resource" : "external-url",
                    $"{x.Groups[2].Value} is outside the disc (the menu has no network, §10)"));
        foreach (Match x in CssUrl().Matches(scripts + html))
            if (x.Groups[1].Value.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !x.Groups[1].Value.StartsWith("https://disc.local/", StringComparison.OrdinalIgnoreCase))
                issues.Add(new(LintSeverity.Error, "external-resource", $"{x.Groups[1].Value} is outside the disc"));

        foreach (Match x in LocalRef().Matches(html))
        {
            var path = x.Groups[2].Value.Split('?', '#')[0];
            if (path.Length == 0 || path.StartsWith("data:", StringComparison.Ordinal) || path.StartsWith("blob:", StringComparison.Ordinal)) continue;
            if (!assets.ContainsKey(Uri.UnescapeDataString(path)))
                issues.Add(new(LintSeverity.Error, "missing-asset", $"{path} is not in the theme package"));
        }

        var budget = (theme.Theme.Budget?.MaxAssetsMb ?? 15) * 1024 * 1024;
        var size = assets.Values.Sum(a => (long)a.Length);
        if (size > budget) issues.Add(new(LintSeverity.Error, "asset-budget", $"assets are {size / 1048576.0:0.0} MB, budget is {budget / 1048576.0:0.#} MB"));

        var caps = theme.Theme.Capabilities?.ToHashSet() ?? [];
        foreach (Match x in SdkCall().Matches(scripts))
            if (SdkCalls.TryGetValue(x.Groups[1].Value, out var cap) && !caps.Contains(cap))
                issues.Add(new(LintSeverity.Warning, "capability", $"disc.{x.Groups[1].Value} is used but \"{cap.ToString().ToLowerInvariant()}\" is not in capabilities"));

        foreach (var (key, value) in settings)
            if (value is string fg && Hex().IsMatch(fg) && theme.Theme.Settings![key].Type == SettingType.Color && key is not ("bg" or "background"))
            {
                var ratio = Wcag.Contrast(fg[..7], m.Theme.Palette.Bg);
                if (ratio < 3) issues.Add(new(LintSeverity.Warning, "contrast", $"setting {key} ({fg}) on bg {m.Theme.Palette.Bg} is {ratio:0.0}:1"));
            }
        if (Wcag.Contrast(m.Theme.Palette.Text, m.Theme.Palette.Bg) < 4.5)
            issues.Add(new(LintSeverity.Warning, "contrast", $"text on bg is {Wcag.Contrast(m.Theme.Palette.Text, m.Theme.Palette.Bg):0.0}:1"));
        return issues.DistinctBy(i => (i.Rule, i.Message)).ToList();
    }

    [GeneratedRegex(@"disc\.run\(\s*['""]([^'""]+)['""]")] private static partial Regex RunCall();
    [GeneratedRegex(@"data-action\s*=\s*""([^""]+)""")] private static partial Regex DataAction();
    [GeneratedRegex(@"\b(src|href|action|poster)\s*=\s*""((?:https?:)?//[^""]+)""", RegexOptions.IgnoreCase)] private static partial Regex ExternalUrl();
    [GeneratedRegex(@"url\(\s*['""]?([^'"")]+)")] private static partial Regex CssUrl();
    [GeneratedRegex(@"\b(src|href)\s*=\s*""(?!https?:|//|#|data:|blob:|javascript:|mailto:)([^""{}]+)""", RegexOptions.IgnoreCase)] private static partial Regex LocalRef();
    [GeneratedRegex(@"\bdisc\.(\w+)\(")] private static partial Regex SdkCall();
    [GeneratedRegex("^#[0-9a-fA-F]{6}")] private static partial Regex Hex();
}
