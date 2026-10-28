using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Inlay.Core;
using Json.Schema;
using Xunit;

namespace Inlay.Core.Tests;

public class ContractTests
{
    static readonly JsonSchema ManifestSchema = JsonSchema.FromFile(Path.GetFullPath("schemas/manifest.schema.json"));
    static readonly JsonSchema ThemeSchema = JsonSchema.FromFile(Path.GetFullPath("schemas/theme.schema.json"));

    public static TheoryData<string> Manifests => new(Directory.GetFiles("samples", "manifest.*.json"));
    public static TheoryData<string> Themes => new(Directory.GetFiles("samples", "theme.*.json"));

    [Theory, MemberData(nameof(Manifests))]
    public void Manifest_sample_round_trips_losslessly(string path) =>
        AssertRoundTrip(path, ManifestSchema,
            json => JsonSerializer.Serialize(JsonSerializer.Deserialize(json, InlayJson.Default.DiscManifest)!, InlayJson.Default.DiscManifest));

    [Theory, MemberData(nameof(Themes))]
    public void Theme_sample_round_trips_losslessly(string path) =>
        AssertRoundTrip(path, ThemeSchema,
            json => JsonSerializer.Serialize(JsonSerializer.Deserialize(json, InlayJson.Default.ThemePackage)!, InlayJson.Default.ThemePackage));

    static void AssertRoundTrip(string path, JsonSchema schema, Func<string, string> roundTrip)
    {
        var original = File.ReadAllText(path);
        var written = roundTrip(original);

        JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written))
            .Should().BeTrue($"serializing {path} back must reproduce it exactly:\n{written}");
        IsValid(schema, written).Should().BeTrue("C# output must satisfy the schema");
    }

    [Fact]
    public void Deserializer_rejects_unknown_field()
    {
        var m = Bundle();
        m["items"]![0]!["surprise"] = true;
        Parse(m).Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserializer_rejects_missing_required_field()
    {
        var m = Bundle();
        m["disc"]!.AsObject().Remove("home");
        Parse(m).Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserializer_rejects_unknown_enum_value()
    {
        var m = Bundle();
        m["actions"]![0]!["kind"] = "format-c";
        Parse(m).Should().Throw<JsonException>();
    }

    [Fact]
    public void Item_type_selects_the_derived_record()
    {
        var m = JsonSerializer.Deserialize(File.ReadAllText("samples/manifest.mixed.json"), InlayJson.Default.DiscManifest)!;
        m.Items.Select(i => i.GetType().Name).Should().Contain(["Movie", "Album", "Gallery", "App", "FileItem"]);
    }

    [Fact]
    public void Date_time_keeps_its_offset()
    {
        var local = new DateTimeOffset(2025, 3, 14, 13, 22, 0, TimeSpan.FromHours(3));
        Write(local).Should().Be("2025-03-14T13:22:00+03:00");
        Write(local.ToUniversalTime()).Should().Be("2025-03-14T10:22:00Z");
    }

    static string Write(DateTimeOffset v) =>
        JsonNode.Parse(JsonSerializer.Serialize(v, InlayJson.Default.DateTimeOffset))!.GetValue<string>();
    static JsonNode Bundle() => JsonNode.Parse(File.ReadAllText("samples/manifest.bundle.json"))!;
    static Action Parse(JsonNode n) => () => JsonSerializer.Deserialize(n.ToJsonString(), InlayJson.Default.DiscManifest);

    static bool IsValid(JsonSchema schema, string json) =>
        schema.Evaluate(JsonDocument.Parse(json).RootElement, new EvaluationOptions { RequireFormatValidation = true }).IsValid;
}
