using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kapso.Webhooks.Models;

/// <summary>
/// Reads WhatsApp's <c>"1730092800"</c> timestamps: Unix seconds, as a JSON string.
/// </summary>
/// <remarks>
/// A webhook body mixes three timestamp formats. Two are ISO 8601 and need nothing
/// special — UTC with microseconds under <c>conversation.kapso</c>, and an offset
/// form everywhere else. This handles the third, which appears on
/// <c>message.timestamp</c> and on each entry of <c>message.kapso.statuses</c>
/// because those come straight from Meta.
///
/// A bare JSON number is accepted too, in case Kapso ever stops quoting it.
/// </remarks>
internal sealed class UnixSecondsStringConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.Number when reader.TryGetInt64(out var numeric):
                return DateTimeOffset.FromUnixTimeSeconds(numeric);

            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrEmpty(text))
                {
                    return null;
                }

                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(seconds);
                }

                // Tolerate an ISO value arriving where epoch seconds were expected,
                // rather than failing the whole delivery over one field.
                return DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed)
                        ? parsed
                        : null;

            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(
            value.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }
}
