
namespace Olspy;

/// <summary>
///  Thrown when the overleaf server returns an unexpected message
/// </summary>
public abstract class OverleafException(string message, Exception? inner = null) : Exception(message, inner)
{ }