using System.Text.Json;
using System.Text.Json.Serialization;
using BidEuchre.Core;
namespace BidEuchre.App;

internal static class CampaignJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new FeatureConverter<EnhancementId>(FeatureCategory.Enhancement));
        options.Converters.Add(new FeatureConverter<SigilId>(FeatureCategory.Sigil));
        options.Converters.Add(new FeatureConverter<JokerId>(FeatureCategory.Joker));
        options.Converters.Add(new FeatureConverter<PlanetId>(FeatureCategory.Planet));
        options.Converters.Add(new FeatureConverter<TarotId>(FeatureCategory.Tarot));
        return options;
    }
    private sealed class FeatureConverter<T>(FeatureCategory category) : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected stable feature ID.");
            var id = reader.GetString(); var feature = FeatureCatalog.All.SingleOrDefault(f => f.Category == category && f.Id == id);
            return feature is null ? throw new JsonException("Unknown feature ID.") : Enum.Parse<T>(feature.Key);
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(FeatureCatalog.Get(category, value.ToString()).Id);
    }
}
