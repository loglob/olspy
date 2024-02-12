using System.Text.Json;
using System.Text.Json.Serialization;

namespace Olspy.Util;

/// <summary>
///  Converts between DateTime and a millisecond-precision UNIX timestamp 
/// </summary>
internal class TimeStampConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.UnixEpoch.AddMilliseconds(reader.GetInt64());

	public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteNumberValue((long)(value - DateTime.UnixEpoch).TotalMilliseconds);
}