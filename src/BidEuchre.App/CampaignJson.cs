using System.Text.Json;
using System.Text.Json.Serialization;
using BidEuchre.Core;
namespace BidEuchre.App;

internal static class CampaignJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };
        options.Converters.Add(new FeatureConverter<EnhancementId>(FeatureCategory.Enhancement));
        options.Converters.Add(new FeatureConverter<SigilId>(FeatureCategory.Sigil));
        options.Converters.Add(new FeatureConverter<JokerId>(FeatureCategory.Joker));
        options.Converters.Add(new FeatureConverter<GumballId>(FeatureCategory.Gumball));
        options.Converters.Add(new JsonStringEnumConverter<SodaId>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
        options.Converters.Add(new FeatureConverter<TarotId>(FeatureCategory.Tarot));
        return options;
    }
    private sealed class FeatureConverter<T>(FeatureCategory category) : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected stable feature ID.");
            return Parse(reader.GetString());
        }
        private T Parse(string? id)
        {
            // Gumballs were Planets in runs saved before the rename.
            if (category == FeatureCategory.Gumball && id is not null && id.StartsWith("planet:")) id = "gumball:" + id[7..];
            var feature = FeatureCatalog.All.SingleOrDefault(f => f.Category == category && f.Id == id);
            return feature is null ? throw new JsonException("Unknown feature ID.") : Enum.Parse<T>(feature.Key);
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(FeatureCatalog.Get(category, value.ToString()).Id);
        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => Parse(reader.GetString());
        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WritePropertyName(FeatureCatalog.Get(category, value.ToString()).Id);
    }
}
