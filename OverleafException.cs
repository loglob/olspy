using System.Text.Json.Nodes;

namespace Olspy;

/// <summary>
///  An exception retrieved from the overleaf server
/// </summary>
public class OverleafException : Exception
{

	public OverleafException(string reason, JsonNode response) : base(reason + ": " + response)
	{}
}