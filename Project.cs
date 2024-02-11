using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;

namespace Olspy;

// Disable warning to use an arcane optimization to compile regex at compile time
#pragma warning disable SYSLIB1045

public sealed class Project
{
	/// <summary>
	///  The cookie that stores Overleaf session tokens
	/// </summary>
	private const string SESSION_COOKIE = "sharelatex.sid";
	
	private const string CSRF_HEADER = "X-CSRF-TOKEN";

	/// <summary>
	///  The pattern of read/write join tokens.
	///  (taken from Overleaf source code, could possibly change in future Overleaf versions)
	/// </summary>
	private static readonly Regex ReadWriteTokenPattern = new("^[0-9]+[a-z]{6,12}$");

	/// <summary>
	///  The pattern of read only join tokens.
	///  (taken from Overleaf source code, could possibly change in future Overleaf versions)
	/// </summary>
	private static readonly Regex ReadOnlyTokenPattern = new("^[a-z]{12}$");

	private static readonly Regex CsrfMetaTag = new("<meta +name=\"ol-csrfToken\" *content=\"([^\"]+)\" *>");

	private static string getCsrf(string html)
		=> CsrfMetaTag.Match(html).Groups[1].Value;

	/// <summary>
	///  A globally unique ID identifying this project
	/// </summary>
	public readonly string ID;

	/// <summary>
	///  Cached project information
	/// </summary>
	internal Protocol.JoinProjectArgs? info = null;

	/// <summary>
	///  The http client to make requests through.
	///  Configured with base address, (possibly) a proxy and a CSRF token header.
	/// </summary>
	private readonly HttpClient client;

	private Project(string id, HttpClient client)
	{
		this.ID = id;
		this.client = client;
	}

	/// <returns> Whether a scheme is valid for an overleaf link </returns>
	private static bool validScheme(string scheme)
		=> scheme switch {
			"http" => true,
			"https" => true,
			_ => false
		};

	/// <summary>
	///  Opens a share link, which may be either read/write or read only
	/// </summary>
	public static async Task<Project> Open(Uri shareLink, WebProxy? proxy = null)
	{
		ArgumentNullException.ThrowIfNull(shareLink);

		if(! validScheme(shareLink.Scheme))
			throw new FormatException($"Illegal URI scheme '{shareLink.Scheme}'");
		if(! shareLink.IsAbsoluteUri)
			throw new ArgumentException("Expected an absolute URI", nameof(shareLink));
		if(shareLink.Query.Length > 0)
			throw new FormatException("Share link shouldn't have a query");

		var seg = shareLink.Segments;
		int omit;

		if(seg[0] != "/")
			throw new ArgumentException("Expected an absolute URI", nameof(shareLink));

		if(seg.Length >= 2 && seg[^2] == "read/" && ReadOnlyTokenPattern.IsMatch(seg[^1]))
			omit = 2;
		else if(seg.Length >= 1 && ReadWriteTokenPattern.IsMatch(seg[^1]))
			omit = 1;
		else
			throw new FormatException($"Invalid share URL: {shareLink}");

		var handler = new HttpClientHandler() {
			Proxy = proxy,
			UseProxy = proxy is not null
		};

		var client = new HttpClient(handler) {
			// note: how does this survive escaping?
			BaseAddress = new Uri(shareLink, string.Join("", seg.SkipLast(omit)))
		};

		var req = await client.GetAsync(shareLink);

		if(! req.IsSuccessStatusCode)
			throw new Exception($"Could not GET share link: response code {req.StatusCode})");
		if(handler.CookieContainer.GetAllCookies()[SESSION_COOKIE] is null)
			throw new Exception("Did not receive a session cookie from share link");

		// note: reading entire body into string first is suboptimal, but the response is ~30K so it doesn't really matter
		var csrf = getCsrf(await req.Content.ReadAsStringAsync());

		client.DefaultRequestHeaders.Add(CSRF_HEADER, csrf);

		var grant = await client.PostAsJsonAsync(
			shareLink.AbsolutePath + "/grant",
			new{ _csrf = csrf, confirmedByUser = false }
		);

		if(! grant.IsSuccessStatusCode)
			throw new Exception($"Request for session grant did not succeed, got {grant.StatusCode}");

		var grantC = await grant.Content.ReadFromJsonAsync<Dictionary<string, string>>();

		if(grantC is null || !grantC.TryGetValue("redirect", out var red))
			throw new Exception("Join grant did not contain a project redirect URL");

		// note: I don't know what `red` is relative to if the server has some base prefix
		var id = new Regex("^/project/([0-9a-fA-F]+)$").Match(red).Groups[1].Value;
		
		return new Project(id, client);
	}

	/// <summary>
	///  Opens a project with a user's session token
	/// </summary>
	/// <param name="host"> An uri to the root of the Overleaf site </param>
	/// <param name="ID"> A project ID </param>
	/// <param name="session"> The value of the SESSION_COOKIE cookie </param>
	/// <param name="proxy"> A proxy to use, if any </param>
	public static async Task<Project> Open(Uri host, string ID, string session, WebProxy? proxy = null)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(ID);
		ArgumentNullException.ThrowIfNull(session);

		if(! ID.All(char.IsAsciiHexDigitLower) || ID.Length == 0)
			throw new FormatException($"Expected a hexadecimal project ID, got \"{ID}\"");
		if(! validScheme(host.Scheme))
			throw new FormatException($"Illegal URI scheme '{host.Scheme}'");

		var handler = new HttpClientHandler() {
			Proxy = proxy ,
			UseProxy = proxy is not null
		};
		handler.CookieContainer.Add(new Cookie( SESSION_COOKIE, session ));

		var client = new HttpClient(handler) {
			BaseAddress = host
		};

		var doc = await client.GetAsync($"project/{ID}");

		if(! doc.IsSuccessStatusCode)
			throw new Exception("Failed to load project page; Are the credentials correct?");

		// note: reading into string is suboptimal, the page is ~70K
		var csrf = getCsrf(await doc.Content.ReadAsStringAsync());

		client.DefaultRequestHeaders.Add(CSRF_HEADER, csrf);

		return new Project(ID, client);
	}

	public static async Task<Project> Open(Uri host, string ID, string email, string password, WebProxy? proxy = null)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(ID);
		ArgumentNullException.ThrowIfNull(email);
		ArgumentNullException.ThrowIfNull(password);

		if(! ID.All(char.IsAsciiHexDigitLower) || ID.Length == 0)
			throw new FormatException($"Expected a hexadecimal project ID, got \"{ID}\"");
		if(! validScheme(host.Scheme))
			throw new FormatException($"Illegal URI scheme '{host.Scheme}'");

		var handler = new HttpClientHandler() {
			Proxy = proxy ,
			UseProxy = proxy is not null
		};
		var client = new HttpClient(handler) {
			BaseAddress = host
		};

		var loginPage = await client.GetAsync("login");

		if(! loginPage.IsSuccessStatusCode)
			throw new Exception($"Could not GET login page, got code {loginPage.StatusCode}. Is the host URI correct?");

		var csrf = getCsrf(await loginPage.Content.ReadAsStringAsync());
		client.DefaultRequestHeaders.Add(CSRF_HEADER, csrf);

		var login = await client.PostAsJsonAsync("login", new{
			_csrf = csrf,
			email,
			password
		});

		if(! login.IsSuccessStatusCode)
			throw new Exception($"Failed to log in with code {login.StatusCode}. Are the credentials correct?");
		if(handler.CookieContainer.GetAllCookies()[SESSION_COOKIE] is null)
			throw new Exception("Did not receive a session cookie after login");
		
		return new Project(ID, client);
	}

	/// <summary>
	///  Compiles the project
	/// </summary>
	/// <param name="rootDoc"> The tex document to use as main file. Use null for the default main file. </param>
	/// <param name="draft"> Whether to run a draft compile </param>
	/// <param name="check"> Observed values: silent </param>
	/// <param name="incremental"></param>
	/// <param name="stopOnFirstError"> Whether to stop on error or continue compiling </param>
	/// <returns></returns>
	public async Task<Protocol.CompileInfo> Compile(string? rootDoc = null, bool draft = false, string check = "silent", bool incremental = true, bool stopOnFirstError = false)
	{
		var res = await client.PostAsJsonAsync($"project/{ID}/compile?",
			new{ rootDoc_id = rootDoc, draft, check, incrementalCompilesEnabled = incremental, stopOnFirstError });

		if(! res.IsSuccessStatusCode)
			throw new Exception($"Bad status code requesting compile: got {res.StatusCode}");
		
		return (await res.Content.ReadFromJsonAsync<Protocol.CompileInfo>(Protocol.JsonOptions)) ?? throw new Exception("Compile API returned null object", null);
	}

	public async Task<string[]> GetDocumentByID(string docID)
	{
		await using var s = await Join();
		return await s.GetDocumentByID(docID);
	}

	/// <summary>
	///  Retrieves general project information, containing its file structure
	/// </summary>
	/// <param name="cache"> If false, do not return cached information but always make a new web request </param>
	public async ValueTask<Protocol.JoinProjectArgs> GetInfo(bool cache = true)
	{
		if(!cache || info is null)
		{
			await using var jp = await Join();
			return await jp.GetProjectInfo();
		}
		else
			return info;
	}

	/// <summary>
	///  Retrieves a file from a previously successful compilation
	/// </summary>
	/// <param name="f"> A file listed in the record returned by Compile() </param>
	public async Task<HttpContent> GetOutFile(Protocol.OutputFile f)
	{
		var resp = await client.GetAsync($"project/{ID}/build/{f.Build}/output/{f.Path}");

		if(! resp.IsSuccessStatusCode)
			throw new Exception("Failed to GET compilation result");
		
		return resp.Content;
	}

	/// <summary>
	///  Initializes a websocket instance for this project
	/// </summary>
	public Task<ProjectSession> Join()
		=> ProjectSession.Connect(this, client);

}
