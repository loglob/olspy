using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Olspy.Util;

/// <summary>
///  Parses the custom format of project_ops entries
/// </summary>
public class ProjectOpConverter : JsonConverter<Protocol.BaseProjectOp>
{
	private record BaseArgs(string pathname);
	private record RenameArgs(string pathname, string newPathname) : BaseArgs(pathname);

	public override Protocol.BaseProjectOp? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if(reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException($"project_ops entries must be objects, got a {reader.TokenType}");

		uint? atV = null;
		Func<uint, Protocol.BaseProjectOp>? fin = null;

		while(reader.Read())
		{
			switch(reader.TokenType)
			{
				case JsonTokenType.PropertyName:
				{
					if(reader.ValueTextEquals("atV"))
					{
						if(! reader.Read())
							throw new JsonException("Missing value for property 'atV'");
						if(atV.HasValue)
							throw new JsonException("Duplicate 'atV' value");

						atV = reader.GetUInt32();
					}
					else if(reader.ValueTextEquals("add") || reader.ValueTextEquals("remove"))
					{
						bool add = reader.ValueTextEquals("add");

						if(! reader.Read())
							throw new JsonException($"Missing value for property '{(add ? "add" : "remove")}'");
						if(fin is not null)
							throw new JsonException("Duplicate add, remove or rename value");

						var args = JsonSerializer.Deserialize<BaseArgs>(ref reader, options);

						if(args is null)
							throw new JsonException($"Property '{(add ? "add" : "remove")}' cannot be null");

						fin = a => new Protocol.AddProjectOp(a, args.pathname);
					}
					else if(reader.ValueTextEquals("rename"))
					{
						if(! reader.Read())
							throw new JsonException("Missing value for property 'rename'");
						if(fin is not null)
							throw new JsonException("Duplicate add, remove or rename value");

						var args = JsonSerializer.Deserialize<RenameArgs>(ref reader, new JsonSerializerOptions(options) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

						if(args is null)
							throw new JsonException($"Property 'rename' cannot be null");

						fin = a => new Protocol.RenameProjectOp(a, args.pathname, args.newPathname);
					}
					else
						throw new JsonException($"Invalid property name: {Encoding.UTF8.GetString(reader.ValueSpan.ToArray())}");
				}
				break;

				case JsonTokenType.Comment:
					break;

				case JsonTokenType.EndObject:
				{
					if(! atV.HasValue)
						throw new JsonException("Expected an 'atV' property");
					if(fin is null)
						throw new JsonException("Expected a 'add', 'remove' or 'rename' property");
					
					// don't Read(), that leads to an exception
					return fin(atV.Value);
				}

				default:
					throw new JsonException("Unexpected token type: " + reader.TokenType);
			}
		}

		throw new JsonException("Object was never closed");
	}

	public override void Write(Utf8JsonWriter writer, Protocol.BaseProjectOp value, JsonSerializerOptions options)
	{
		var (kind, val) = value.Distinguish(a => ("add", new BaseArgs(a.Path)), r => ("remove", new BaseArgs(r.Path)), rn => ("rename", new RenameArgs(rn.Path, rn.NewPath)));

		JsonSerializer.Serialize(writer, new Dictionary<string, object>(){ { kind, val }, { "atV", value.AtV } }, options);
	}
}