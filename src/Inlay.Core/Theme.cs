// Mirrors schemas/theme.schema.json (theme.json at the root of a .disctheme package).
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inlay.Core;

public sealed record ThemePackage
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string? License { get; init; }
    public string? Preview { get; init; }
    public required IReadOnlyList<ThemeMode> Modes { get; init; }
    public required ThemeEngine Engine { get; init; }
    public required string Entry { get; init; }
    public IReadOnlyList<string>? Partials { get; init; }
    public required string MinLauncher { get; init; }
    public IReadOnlyList<ThemeCapability>? Capabilities { get; init; }
    public IReadOnlyList<ThemeFont>? Fonts { get; init; }
    public IReadOnlyDictionary<string, ThemeSetting>? Settings { get; init; }
    public ThemeSections? Sections { get; init; }
    public ThemeBudget? Budget { get; init; }
}

public sealed record ThemeFont
{
    public required string Family { get; init; }
    public required string File { get; init; }
    public string? Weight { get; init; }
    public FontStyle? Style { get; init; }
}

// The schema leaves setting objects open (no additionalProperties:false), so unknown keys are tolerated here too.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed record ThemeSetting
{
    public required SettingType Type { get; init; }
    public required string Label { get; init; }
    public string? Help { get; init; }
    public string? Group { get; init; }
    public JsonElement? Default { get; init; }
    public IReadOnlyList<SettingOption>? Options { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public string? Unit { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed record SettingOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}

public sealed record ThemeSections
{
    public SectionDefault? Movie { get; init; }
    public SectionDefault? Album { get; init; }
    public SectionDefault? Gallery { get; init; }
    public SectionDefault? App { get; init; }
    public SectionDefault? File { get; init; }
}

public sealed record SectionDefault
{
    public SectionLayout? Layout { get; init; }
    public string? Title { get; init; }
}

public sealed record ThemeBudget
{
    public double? MaxAssetsMb { get; init; }
    public int? MaxFirstPaintMs { get; init; }
}

[JsonConverter(typeof(CamelCaseEnum<ThemeMode>))] public enum ThemeMode { Mixed, Movie, Album, Gallery, App }
[JsonConverter(typeof(CamelCaseEnum<ThemeEngine>))] public enum ThemeEngine { Liquid, Static }
[JsonConverter(typeof(CamelCaseEnum<FontStyle>))] public enum FontStyle { Normal, Italic }
[JsonConverter(typeof(CamelCaseEnum<SettingType>))] public enum SettingType { Color, Bool, Font, Select, Range, Image, Text }
[JsonConverter(typeof(CamelCaseEnum<ThemeCapability>))]
public enum ThemeCapability { Play, Playlist, Slideshow, Run, Install, OpenFolder, Url, Gamepad, Intro, Fullscreen }
