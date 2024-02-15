using System.Text.Json.Nodes;

namespace Olspy;

/// <summary>
///  Thrown when an error is received through the websocket API
/// </summary>
public class WebSocketException(string reason, JsonNode? response, Exception? inner = null) : OverleafException(reason + ": " + (response?.ToString() ?? "null"), inner)
{}
