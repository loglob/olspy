using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Olspy;

/// <summary>
///  Thrown when an error is received through the websocket API
/// </summary>
public class WebSocketException(string reason, JsonNode? response, Exception? inner = null) : OverleafException(reason, inner)
{
	private static readonly JsonSerializerOptions printOptions = new() {
		WriteIndented = true,
		IndentCharacter = '\t',
		IndentSize = 1,
		NewLine = "\n" // cannot be \n\t
	};

	public override string ToString()
	{
		var sb = new StringBuilder(Message);

		if(response is not null)
		{
			foreach(var line in response.ToJsonString(printOptions).Split('\n'))
				sb.Append("\n\t").Append(line);
		}

		if(InnerException is not null)
			sb.Append("\n\n ---> ").Append(InnerException.GetType().Name).Append(": ").Append(InnerException.Message);

		return sb.ToString();
	}
}
