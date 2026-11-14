using System.Text.Json;
using System.Text.Json.Serialization;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Config;

/// <summary>
/// Reads both the current shape (<c>template</c>/<c>engine</c>) and the legacy one (<c>booruType</c>, from the
/// first Java versions), like the Java BooruConfigurationAdapter. Unknown enum values yield null, so one bad
/// entry cannot make the whole file unreadable; <see cref="ConfigManager"/> drops those.
/// </summary>
public sealed class BooruConfigurationConverter : JsonConverter<BooruConfiguration?>
{
    public override BooruConfiguration? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var obj = doc.RootElement;
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (obj.TryGetProperty("template", out _) || obj.TryGetProperty("engine", out _))
        {
            return new BooruConfiguration
            {
                Template = Enum<BooruTemplate>(obj, "template"),
                Engine = Enum<BooruEngine>(obj, "engine"),
                Flavor = Enum<BooruFlavor>(obj, "flavor"),
                CustomBaseUrl = Str(obj, "customBaseUrl"),
                ApiKey = Str(obj, "apiKey"),
                UserId = Str(obj, "userId"),
            };
        }
        return MigrateLegacy(obj);
    }

    public override void Write(Utf8JsonWriter writer, BooruConfiguration? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStartObject();
        WriteEnum(writer, "template", value.Template);
        WriteEnum(writer, "engine", value.Engine);
        WriteEnum(writer, "flavor", value.Flavor);
        WriteStr(writer, "customBaseUrl", value.CustomBaseUrl);
        WriteStr(writer, "apiKey", value.ApiKey);
        WriteStr(writer, "userId", value.UserId);
        writer.WriteEndObject();
    }

    private static BooruConfiguration MigrateLegacy(JsonElement obj)
    {
        var legacyType = Str(obj, "booruType") ?? "GENERIC";
        return new BooruConfiguration
        {
            Template = legacyType switch
            {
                "GELBOORU" => BooruTemplate.Gelbooru,
                "SAFEBOORU" => BooruTemplate.Safebooru,
                "DANBOORU" => BooruTemplate.Danbooru,
                _ => null,
            },
            CustomBaseUrl = Str(obj, "customBaseUrl"),
            ApiKey = Str(obj, "apiKey"),
            UserId = legacyType == "DANBOORU" ? Str(obj, "login") : Str(obj, "userId"),
        };
    }

    private static string? Str(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static T? Enum<T>(JsonElement obj, string key) where T : struct, Enum
    {
        var text = Str(obj, key);
        if (text is null)
        {
            return null;
        }
        // "GELBOORU_FAMILY" -> GelbooruFamily
        var pascal = string.Concat(text.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        return System.Enum.TryParse<T>(pascal, true, out var result) ? result : null;
    }

    private static void WriteEnum<T>(Utf8JsonWriter writer, string name, T? value) where T : struct, Enum
    {
        if (value is { } v)
        {
            writer.WriteString(name, JsonNamingPolicy.SnakeCaseUpper.ConvertName(v.ToString()));
        }
    }

    private static void WriteStr(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
