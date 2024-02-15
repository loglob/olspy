using System.Net;

namespace Olspy;

/// <summary>
///  Thrown when a HTTP response returned an undesired status code
/// </summary>
public class HttpStatusException(HttpStatusCode statusCode, string? reasonPhrase, string message)
	: OverleafException($"Received status code {(int)statusCode} ({getReasonPhrase(reasonPhrase, statusCode)}) {message}")
{
	private static string getReasonPhrase(string? reasonPhrase, HttpStatusCode statusCode)
		=> reasonPhrase ?? Enum.GetName(statusCode) ?? "Unknown";

	public readonly HttpStatusCode StatusCode = statusCode;
	public readonly string ReasonPhrase = getReasonPhrase(reasonPhrase, statusCode);

	/// <exception cref="HttpStatusException"> If the status code of `response` indicates failure </exception>
	public static void ThrowUnlessSuccessful(HttpResponseMessage response, string message)
	{
		if(! response.IsSuccessStatusCode)
			throw new HttpStatusException(response.StatusCode, response.ReasonPhrase, message);
	}
}