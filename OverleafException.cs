using System.Text.Json.Nodes;

namespace Olspy;

/// <summary>
///  An exception received from the overleaf server
/// </summary>
public class OverleafException(string reason, JsonNode? response) : Exception(reason + ": " + (response?.ToString() ?? "null"))
{}