using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inlay.Core;

/// <summary>Source-generated (AOT-safe) serializer for every contract type. Strict like the schemas:
/// unknown fields, missing required fields and nulls in non-nullable fields are rejected.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    AllowOutOfOrderMetadataProperties = true,
    RespectNullableAnnotations = true,
    WriteIndented = true,
    Converters = [typeof(DateTimeOffsetConverter)])]
[JsonSerializable(typeof(DiscManifest))]
[JsonSerializable(typeof(ThemePackage))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
public sealed partial class InlayJson : JsonSerializerContext;

public sealed class CamelCaseEnum<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where T : struct, Enum;

/// <summary>Writes RFC 3339 date-times the way the samples and other tools do ("...Z" for UTC).</summary>
public sealed class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    // Keeps the offset: EXIF takenAt is local wall-clock time, converting it to UTC would shift the shown hour.
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(
            value.Offset == TimeSpan.Zero ? "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            CultureInfo.InvariantCulture));
}
