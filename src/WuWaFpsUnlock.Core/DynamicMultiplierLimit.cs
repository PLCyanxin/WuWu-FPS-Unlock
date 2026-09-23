using System.Text.Json;
using System.Text.Json.Serialization;

namespace WuWaFpsUnlock.Core;

public static class DynamicMultiplierLimit
{
    public static int Normalize(int value) => value is 0 or >= 2 and <= 6 ? value : 0;
}

// Invalid field values disable the override rather than enabling a clamped limit.
// Missing properties retain UserSettings' default; malformed JSON syntax still fails.
public sealed class DynamicMultiplierLimitJsonConverter : JsonConverter<int>
{
    public override bool HandleNull => true;
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetInt32(out int value) ? DynamicMultiplierLimit.Normalize(value) : 0;
        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
        {
            using var ignored = JsonDocument.ParseValue(ref reader);
        }
        return 0;
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(DynamicMultiplierLimit.Normalize(value));
}
