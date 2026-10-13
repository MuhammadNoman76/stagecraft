using System.Text.Json;
using System.Text.Json.Serialization;
using Stagecraft.Core.Model;

namespace Stagecraft.Core.IO;

/// <summary>JSON settings for deck.json (camelCase, enums as words, nulls left out) and deep copies through JSON.</summary>
public static class DeckJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: true);

    private static readonly JsonSerializerOptions Compact = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            AllowOutOfOrderMetadataProperties = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Serialize(Deck deck) => JsonSerializer.Serialize(deck, Options);

    public static Deck? Deserialize(string json) => JsonSerializer.Deserialize<Deck>(json, Options);

    /// <summary>A deep copy (through JSON, so nothing is forgotten when the model grows).</summary>
    public static T Clone<T>(T value) where T : class
    {
        var json = JsonSerializer.Serialize(value, Compact);
        return JsonSerializer.Deserialize<T>(json, Compact)!;
    }

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Compact);

    public static T? FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Compact);
}
