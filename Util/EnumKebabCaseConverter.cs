using System.Text.Json;
using System.Text.Json.Serialization;

namespace Olspy.Util;

/// <summary>
///  Converts enum values by name using lower-kebab-case instead of the current naming policy
/// </summary>
public class EnumKebabCaseConverter<T>() : JsonConverter<T> where T : struct, Enum
{
	private static readonly JsonNamingPolicy naming = JsonNamingPolicy.KebabCaseLower;
	private static readonly Dictionary<string, T> values = Enum.GetValues<T>().ToDictionary(v => naming.ConvertName(Enum.GetName<T>(v)!), v => v);

	public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var n = reader.GetString() ?? throw new JsonException($"null value invalid for enum {typeof(T).Name}");
		
		if(values.TryGetValue(n, out var x))
			return x;
		else
			throw new JsonException($"Key '{n}' is not present in enum {typeof(T).Name}");
	}

	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
		=> writer.WriteStringValue(naming.ConvertName(Enum.GetName(value) ?? throw new ArgumentException($"Undefined enum value: {value}")));
	
}