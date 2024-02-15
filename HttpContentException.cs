namespace Olspy;

/// <summary>
///  Thrown when the content returned from an HTTP request contains unexpected data
/// </summary>
public class HttpContentException(string message, Exception? inner = null) : OverleafException(message, inner)
{ }