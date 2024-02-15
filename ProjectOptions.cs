using System.Net;

namespace Olspy;

/// <param name="HttpTimeout"> A timeout for all HTTP requests made. Defaults to the HttpClient default of 100s. </param>
/// <param name="Proxy"> The proxy to use </param>
public record struct ProjectOptions(
	TimeSpan? HttpTimeout = null, 
	WebProxy? Proxy = null
);