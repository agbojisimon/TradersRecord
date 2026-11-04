using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TraderRecords.Api.API.Json;

/// <summary>
/// Requires an explicit UTC designator or numeric offset on occurrence timestamps.
/// System.Text.Json otherwise treats an offset-less value as local time, which would
/// silently accept data the sync contract says is invalid. Returning null for an
/// offset-less or malformed string lets the service return an item-level
/// INVALID_OCCURRED_AT instead of rejecting the whole batch.
/// </summary>
public sealed class RequiredOffsetDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    private static readonly Regex ExplicitOffsetPattern = new(
        @"(?:Z|[+-]\d{2}:?\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public override bool HandleNull => true;

    public override DateTimeOffset? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("OccurredAt must be a string with an explicit UTC offset.");
        }

        var value = reader.GetString()?.Trim();

        if (string.IsNullOrEmpty(value)
            || !ExplicitOffsetPattern.IsMatch(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }

        return parsed;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset? value,
        JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
