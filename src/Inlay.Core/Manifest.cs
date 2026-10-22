// Mirrors schemas/manifest.schema.json. Optional fields stay nullable (schema defaults are applied by consumers)
// so a manifest round-trips without gaining fields it never had. ManifestContractTests keeps this file honest.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inlay.Core;

public sealed record DiscManifest
{
    [JsonPropertyName("$schema")] public string? Schema { get; init; }
    public required int Version { get; init; }
    public required Disc Disc { get; init; }
    public required ThemeRef Theme { get; init; }
    public required IReadOnlyList<Section> Sections { get; init; }
    public required IReadOnlyList<DiscItem> Items { get; init; }
    public required IReadOnlyList<DiscAction> Actions { get; init; }
    public IReadOnlyList<Credit>? Credits { get; init; }
    public IReadOnlyList<License>? Licenses { get; init; }
}

public sealed record Disc
{
    public required string Label { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required DiscMedia Media { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Builder { get; init; }
    public string? Locale { get; init; }
    public string? Icon { get; init; }
    public string? Logo { get; init; }
    public Intro? Intro { get; init; }
    public string? Featured { get; init; }
    public bool? Autoplay { get; init; }
    public bool? Remote { get; init; }
    public required DiscHome Home { get; init; }
}

public sealed record Intro
{
    public string? Video { get; init; }
    public string? Music { get; init; }
    public int? DurationMs { get; init; }
    public bool? Skippable { get; init; }
}

public sealed record DiscHome
{
    public required HomeView View { get; init; }
    public string? Item { get; init; }
    public CollectionKind? Kind { get; init; }
}

public sealed record ThemeRef
{
    public required string Package { get; init; }
    public required string Version { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Settings { get; init; }
    public required Palette Palette { get; init; }
}

public sealed record Palette
{
    public required string Bg { get; init; }
    public required string Surface { get; init; }
    public required string Accent { get; init; }
    public string? AccentText { get; init; }
    public required string Text { get; init; }
    public required string Muted { get; init; }
    public PaletteSource? Source { get; init; }
}

public sealed record Section
{
    public required string Id { get; init; }
    public required SectionKind Kind { get; init; }
    public required string Title { get; init; }
    public SectionLayout? Layout { get; init; }
    public bool? Hidden { get; init; }
    public required IReadOnlyList<string> Items { get; init; }
}

public sealed record Credit
{
    public required string Text { get; init; }
    public string? Logo { get; init; }
    public string? Url { get; init; }
}

public sealed record License
{
    public required string Name { get; init; }
    [JsonPropertyName("license")] public required string Text { get; init; }
    public required string File { get; init; }
}

// ---- media ----

public sealed record MediaFile
{
    public required string File { get; init; }
    public required long SizeBytes { get; init; }
    public string? Container { get; init; }
    public double? DurationSec { get; init; }
    public VideoStream? Video { get; init; }
    public IReadOnlyList<AudioStream>? Audio { get; init; }
    public IReadOnlyList<Subtitle>? Subtitles { get; init; }
    public required Playback Playback { get; init; }
}

public sealed record Playback
{
    public required PlaybackEngine Engine { get; init; }
    public PlaybackFallback? Fallback { get; init; }
    public bool? Normalized { get; init; }
    public string? Original { get; init; }
}

public sealed record VideoStream
{
    public string? Codec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Fps { get; init; }
    public HdrKind? Hdr { get; init; }
}

public sealed record AudioStream
{
    public required string Codec { get; init; }
    public string? Lang { get; init; }
    public double? Channels { get; init; }
    public string? Title { get; init; }
    public bool? Default { get; init; }
}

public sealed record Subtitle
{
    public required string Lang { get; init; }
    public string? File { get; init; }
    public int? Embedded { get; init; }
    public bool? Forced { get; init; }
}

// ---- items (discriminated by "type") ----

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Movie), "movie")]
[JsonDerivedType(typeof(Series), "series")]
[JsonDerivedType(typeof(Album), "album")]
[JsonDerivedType(typeof(Gallery), "gallery")]
[JsonDerivedType(typeof(App), "app")]
[JsonDerivedType(typeof(FileItem), "file")]
public abstract record DiscItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Cover { get; init; }
    public Palette? Palette { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public IReadOnlyList<string>? Actions { get; init; }
}

public sealed record ItemSource
{
    public MetaProvider? Provider { get; init; }
    public string? ExternalId { get; init; }
}

public sealed record Movie : DiscItem
{
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }
    public string? Tagline { get; init; }
    public IReadOnlyList<string>? Genres { get; init; }
    public double? Rating { get; init; }
    public string? Certification { get; init; }
    public string? Backdrop { get; init; }
    public string? Logo { get; init; }
    public IReadOnlyList<CastMember>? Cast { get; init; }
    public IReadOnlyList<CrewMember>? Crew { get; init; }
    public IReadOnlyList<Chapter>? Chapters { get; init; }
    public MediaFile? Trailer { get; init; }
    public required MediaFile Media { get; init; }
    public IReadOnlyList<MediaFile>? Extras { get; init; }
    public ItemSource? Source { get; init; }
}

public sealed record CastMember
{
    public required string Name { get; init; }
    public string? Character { get; init; }
    public string? Photo { get; init; }
}

public sealed record CrewMember
{
    public required string Name { get; init; }
    public required string Job { get; init; }
}

public sealed record Chapter
{
    public required string Title { get; init; }
    public required double StartSec { get; init; }
    public string? Thumb { get; init; }
}

public sealed record Series : DiscItem
{
    public string? Overview { get; init; }
    public IReadOnlyList<string>? Genres { get; init; }
    public int? YearFrom { get; init; }
    public int? YearTo { get; init; }
    public string? Backdrop { get; init; }
    public string? Logo { get; init; }
    public required IReadOnlyList<Season> Seasons { get; init; }
    public ItemSource? Source { get; init; }
}

public sealed record Season
{
    public required int No { get; init; }
    public string? Title { get; init; }
    public string? Poster { get; init; }
    public required IReadOnlyList<Episode> Episodes { get; init; }
}

public sealed record Episode
{
    public required string Id { get; init; }
    public required int No { get; init; }
    public required string Title { get; init; }
    public string? Overview { get; init; }
    public string? Thumb { get; init; }
    public DateOnly? AirDate { get; init; }
    public required MediaFile Media { get; init; }
}

public sealed record Album : DiscItem
{
    public string? Artist { get; init; }
    public int? Year { get; init; }
    public string? Genre { get; init; }
    public string? Label { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string>? Booklet { get; init; }
    public bool? AudioCd { get; init; }
    public required IReadOnlyList<Track> Tracks { get; init; }
}

public sealed record Track
{
    public required string Id { get; init; }
    public required int No { get; init; }
    public int? Disc { get; init; }
    public required string Title { get; init; }
    public string? Artist { get; init; }
    public string? Lyrics { get; init; }
    public required MediaFile Media { get; init; }
}

public sealed record Gallery : DiscItem
{
    public string? Description { get; init; }
    public DateRange? DateRange { get; init; }
    public Slideshow? Slideshow { get; init; }
    public required IReadOnlyList<Photo> Photos { get; init; }
}

public sealed record DateRange
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record Slideshow
{
    public int? IntervalMs { get; init; }
    public string? Music { get; init; }
    public SlideTransition? Transition { get; init; }
}

public sealed record Photo
{
    public required string Id { get; init; }
    public required string File { get; init; }
    public string? Original { get; init; }
    public required string Thumb { get; init; }
    public string? Blurhash { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public PhotoKind? Kind { get; init; }
    public string? Caption { get; init; }
    public DateTimeOffset? TakenAt { get; init; }
    public string? Camera { get; init; }
    public GeoLocation? Location { get; init; }
    public MediaFile? Media { get; init; }
}

public sealed record GeoLocation
{
    public string? Name { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
}

public sealed record App : DiscItem
{
    public AppCategory? Category { get; init; }
    public string? Version { get; init; }
    public string? Publisher { get; init; }
    public string? Icon { get; init; }
    public string? Description { get; init; }
    public string? Backdrop { get; init; }
    public string? Logo { get; init; }
    public IReadOnlyList<string>? Screenshots { get; init; }
    public MediaFile? Trailer { get; init; }
    public long? InstallSizeBytes { get; init; }
    public Requirements? Requirements { get; init; }
    public Signature? Signature { get; init; }
    public BundleInfo? Bundle { get; init; }
}

public sealed record Requirements
{
    public string? Os { get; init; }
    public string? Cpu { get; init; }
    public double? RamGb { get; init; }
    public string? Gpu { get; init; }
    public double? DiskGb { get; init; }
    public string? Directx { get; init; }
    public IReadOnlyList<string>? Prerequisites { get; init; }
}

public sealed record Signature
{
    public bool? Signed { get; init; }
    public string? Signer { get; init; }
}

public sealed record BundleInfo
{
    public bool? Selected { get; init; }
    public bool? Required { get; init; }
    public int? Order { get; init; }
    public IReadOnlyList<string>? SilentArgs { get; init; }
}

public sealed record FileItem : DiscItem
{
    public required string Path { get; init; }
    public string? Mime { get; init; }
    public string? Description { get; init; }
    public long? SizeBytes { get; init; }
}

// ---- actions (the launcher's allow-list) ----

public sealed record DiscAction
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public ActionIcon? Icon { get; init; }
    public required ActionKind Kind { get; init; }
    public string? Target { get; init; }
    public IReadOnlyList<string>? Args { get; init; }
    public string? WorkingDir { get; init; }
    public bool? Elevated { get; init; }
    public string? Confirm { get; init; }
    public CloseLauncher? CloseLauncher { get; init; }
}

// ---- enums (camelCase on the wire, see CamelCaseEnum) ----

[JsonConverter(typeof(CamelCaseEnum<DiscMedia>))] public enum DiscMedia { Cd700, Dvd5, Dvd9 }
[JsonConverter(typeof(CamelCaseEnum<HomeView>))] public enum HomeView { Menu, Item, Collection, Series, Bundle }
[JsonConverter(typeof(CamelCaseEnum<CollectionKind>))] public enum CollectionKind { Movie, Album, App, Gallery }
[JsonConverter(typeof(CamelCaseEnum<SectionKind>))] public enum SectionKind { Movie, Series, Album, Gallery, App, File }
[JsonConverter(typeof(CamelCaseEnum<SectionLayout>))] public enum SectionLayout { Row, Grid, Hero, List }
[JsonConverter(typeof(CamelCaseEnum<PaletteSource>))] public enum PaletteSource { Auto, Manual }
[JsonConverter(typeof(CamelCaseEnum<PlaybackEngine>))] public enum PlaybackEngine { Html, Libvlc, Shell }
[JsonConverter(typeof(CamelCaseEnum<PlaybackFallback>))] public enum PlaybackFallback { Libvlc, Shell, None }
[JsonConverter(typeof(CamelCaseEnum<MetaProvider>))] public enum MetaProvider { Tmdb, Nfo, Manual }
[JsonConverter(typeof(CamelCaseEnum<SlideTransition>))] public enum SlideTransition { Fade, Kenburns, Slide, None }
[JsonConverter(typeof(CamelCaseEnum<PhotoKind>))] public enum PhotoKind { Image, Video, Animated }
[JsonConverter(typeof(CamelCaseEnum<AppCategory>))] public enum AppCategory { Game, App, Driver, Tool, Runtime, Dlc }
[JsonConverter(typeof(CamelCaseEnum<ActionKind>))] public enum ActionKind { Play, Playlist, Slideshow, Install, Run, Open, OpenFolder, Url }
[JsonConverter(typeof(CamelCaseEnum<CloseLauncher>))] public enum CloseLauncher { Never, OnStart, OnExit }
[JsonConverter(typeof(CamelCaseEnum<ActionIcon>))]
public enum ActionIcon { Play, Install, Run, Folder, Document, Web, Music, Image, Info, Trailer }

[JsonConverter(typeof(CamelCaseEnum<HdrKind>))]
public enum HdrKind { None, Hdr10, Hlg, [JsonStringEnumMemberName("dolby-vision")] DolbyVision }
