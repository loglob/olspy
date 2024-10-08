using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Olspy;

// Disable warning to use an arcane optimization to compile regex at compile time
#pragma warning disable SYSLIB1045

public sealed class Project
{
	/// <summary>
	///  The cookie that stores Overleaf session tokens before version 5.0
	/// </summary>
	private const string SESSION_COOKIE = "sharelatex.sid";

	/// <summary>
	///  The cookie that stores Overleaf session tokens after version 5.0
	/// </summary>
	private const string NEW_SESSION_COOKIE = "overleaf.sid";

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
	{
		var gr = CsrfMetaTag.Match(html).Groups;

		if(gr.Count < 2)
			throw new HttpContentException("The received page doesn't contain a CSRF token");

		return gr[1].Value;
	}

	/// <summary>
	///  wraps ReadFromJsonAsync() to throw HttpContentException instead of JsonException
	/// </summary>
	private static async Task<T> getJson<T>(HttpContent content, CancellationToken ct) where T : class
	{
		T? comp;

		try
		{
			comp = await content.ReadFromJsonAsync<T>(Protocol.JsonOptions, ct);
		}
		catch(JsonException je)
		{
			throw new HttpContentException("Server returned invalid JSON", je);
		}

		return comp ?? throw new HttpContentException("Server returned null object");
	}

	/// <returns> Whether a scheme is valid for an overleaf link </returns>
	private static bool validScheme(string scheme)
		=> scheme switch {
			"http" => true,
			"https" => true,
			_ => false
		};

	private static (HttpClientHandler handler, HttpClient client) configureClient(Uri baseUri, ProjectOptions options)
	{
		var handler = new HttpClientHandler() {
			Proxy = options.Proxy,
			UseProxy = options.Proxy is not null
		};
		
		var client = new HttpClient(handler) {
			BaseAddress = baseUri
		};
		
		if(options.HttpTimeout.HasValue)
			client.Timeout = options.HttpTimeout.Value;

		return (handler, client);
	}

	/// <summary>
	///  Opens a share link, which may be either read/write or read only
	/// </summary>
	/// <exception cref="ArgumentNullException"> If `shareLink` is null </exception>
	/// <exception cref="FormatException"> If `shareLink` isn'T in the expected format </exception>
	/// <exception cref="HttpStatusException"> If the server responds with an invalid status code </exception>
	/// <exception cref="HttpContentException"> If the server responds with invalid page content </exception>
	/// <exception cref="HttpRequestException"> If an internal error occurs while making HTTP requests </exception>
	/// <exception cref="TaskCanceledException"> If the configured HttpTimeout is exceeded </exception>
	public static async Task<Project> Open(Uri shareLink, ProjectOptions options = default, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(shareLink);

		if(! validScheme(shareLink.Scheme))
			throw new FormatException($"Illegal URI scheme '{shareLink.Scheme}'");
		if(shareLink.Query.Length > 0)
			throw new FormatException("Share link shouldn't have a query");

		var seg = shareLink.Segments;
		int omit;

		if(seg[0] != "/")
			throw new FormatException("Share link should be an absolute URI");

		if(seg.Length >= 2 && seg[^2] == "read/" && ReadOnlyTokenPattern.IsMatch(seg[^1]))
			omit = 2;
		else if(seg.Length >= 1 && ReadWriteTokenPattern.IsMatch(seg[^1]))
			omit = 1;
		else
			throw new FormatException($"Invalid share URL: {shareLink}");

		var (handler, client) = configureClient(new Uri(shareLink, string.Join("", seg.SkipLast(omit))), options);

		var req = await client.GetAsync(shareLink, ct);

		HttpStatusException.ThrowUnlessSuccessful(req, "trying to GET share link");
		var ck = handler.CookieContainer.GetAllCookies();

		if(ck[SESSION_COOKIE] is null && ck[NEW_SESSION_COOKIE] is null)
			throw new HttpContentException("Did not receive a session cookie from share link");

		// note: reading entire body into string first is suboptimal, but the response is ~30K so it doesn't really matter
		var csrf = getCsrf(await req.Content.ReadAsStringAsync(ct));

		client.DefaultRequestHeaders.Add(CSRF_HEADER, csrf);

		var grant = await client.PostAsJsonAsync(
			shareLink.AbsolutePath + "/grant",
			new{ _csrf = csrf, confirmedByUser = false },
			ct
		);

		HttpStatusException.ThrowUnlessSuccessful(grant, "trying to join project. Is the join link correct?");

		var grantC = await getJson<Dictionary<string, string>>(grant.Content, ct);

		if(! grantC.TryGetValue("redirect", out var red))
			throw new HttpContentException("Join grant did not contain a project redirect URL");

		var redGr = new Regex("/project/([0-9a-fA-F]+)$").Match(red).Groups;

		if(redGr.Count < 2)
			throw new HttpContentException("Project redirect URL from join grant was not in the expected format");

		// note: I don't know what `red` is relative to if the server has some base prefix
		var id = redGr[1].Value;

		return new Project(id, client);
	}

	/// <summary>
	///  Opens a project with a user's session token
	/// </summary>
	/// <param name="host"> An uri to the root of the Overleaf site </param>
	/// <param name="ID"> A project ID </param>
	/// <param name="session"> The value of the SESSION_COOKIE cookie </param>
	/// <param name="proxy"> A proxy to use, if any </param>
	/// <exception cref="ArgumentNullException"> If any argument besides `proxy` is null </exception>
	/// <exception cref="FormatException"> If `ID` or `host` have invalid format </exception>
	/// <exception cref="HttpStatusException"> If the server returns any HTTP errors trying to load the project </exception>
	/// <exception cref="HttpContentException"> If the server returns a page in an invalid format </exception>
	/// <exception cref="HttpRequestException"> If an internal error occurs while making HTTP requests </exception>
 	/// <exception cref="TaskCanceledException"> If the configured HttpTimeout is exceeded </exception>
	public static async Task<Project> Open(Uri host, string ID, string session, ProjectOptions options = default, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(ID);
		ArgumentNullException.ThrowIfNull(session);

		if(! ID.All(char.IsAsciiHexDigitLower) || ID.Length == 0)
			throw new FormatException($"Expected a hexadecimal project ID, got \"{ID}\"");
		if(! validScheme(host.Scheme))
			throw new FormatException($"Illegal URI scheme '{host.Scheme}'");

		var (handler, client) = configureClient(host, options);
		handler.CookieContainer.Add(new Cookie( SESSION_COOKIE, session ));

		var doc = await client.GetAsync($"project/{ID}", ct);
		HttpStatusException.ThrowUnlessSuccessful(doc, "trying to load the project page. Are the credentials correct?");

		// note: reading into string is suboptimal, the page is ~70K
		var csrf = getCsrf(await doc.Content.ReadAsStringAsync(ct));

		client.DefaultRequestHeaders.Add(CSRF_HEADER, csrf);

		return new Project(ID, client);
	}

	/// <summary>
	///  Opens a project with user credentials.
	/// </summary>
	/// <param name="host"> An uri to the root of the Overleaf site </param>
	/// <param name="ID"> The ID of the project to open </param>
	/// <exception cref="ArgumentNullException"> If any argument besides `proxy` is null </exception>
	/// <exception cref="FormatException"> If `ID` or `host` have invalid format </exception>
	/// <exception cref="HttpStatusException"> If the server returns any HTTP errors trying to load the project </exception>
	/// <exception cref="HttpContentException"> If the server returns a page in an invalid format </exception>
	/// <exception cref="HttpRequestException"> If an internal error occurs while making HTTP requests </exception>
	/// <exception cref="TaskCanceledException"> If the configured HttpTimeout is exceeded </exception>
	public static async Task<Project> Open(Uri host, string ID, string email, string password, ProjectOptions options = default, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(ID);
		ArgumentNullException.ThrowIfNull(email);
		ArgumentNullException.ThrowIfNull(password);

		if(! ID.All(char.IsAsciiHexDigitLower) || ID.Length == 0)
			throw new FormatException($"Expected a hexadecimal project ID, got \"{ID}\"");
		if(! validScheme(host.Scheme))
			throw new FormatException($"Illegal URI scheme '{host.Scheme}'");

		var (handler, client) = configureClient(host, options);
		
		var loginPage = await client.GetAsync("login", ct);

		HttpStatusException.ThrowUnlessSuccessful(loginPage, "trying to GET login page. Is the host URI correct?");

		var csrf = getCsrf(await loginPage.Content.ReadAsStringAsync(ct));
		client.DefaultRequestHeaders.Add(CSRF_HEADER, csrf);

		var login = await client.PostAsJsonAsync("login", new{
			_csrf = csrf,
			email,
			password
		}, ct);

		HttpStatusException.ThrowUnlessSuccessful(login, "Trying to log in. Are the credentials correct?");

		var ck = handler.CookieContainer.GetAllCookies();

		if(ck[SESSION_COOKIE] is null && ck[NEW_SESSION_COOKIE] is null)
			throw new Exception("Did not receive a session cookie after login");

		return new Project(ID, client);
	}

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

	/// <summary>
	///  Compiles the project
	/// </summary>
	/// <param name="rootDoc"> The tex document to use as main file. Use null for the default main file. </param>
	/// <param name="draft"> Whether to run a draft compile </param>
	/// <param name="check"> Observed values: silent </param>
	/// <param name="incremental"></param>
	/// <param name="stopOnFirstError"> Whether to stop on error or continue compiling </param>
	/// <exception cref="HttpStatusException"> If the server returns any HTTP errors  </exception>
	/// <exception cref="HttpContentException"> If the server returns invalid JSON data </exception>
	/// <exception cref="HttpRequestException"> If an internal error occurs while making HTTP requests </exception>
	/// <exception cref="TaskCanceledException"> If the configured HttpTimeout is exceeded </exception>
	public async Task<Protocol.CompileInfo> Compile(string? rootDoc = null, bool draft = false, string check = "silent", bool incremental = true, bool stopOnFirstError = false, CancellationToken ct = default)
	{
		var res = await client.PostAsJsonAsync($"project/{ID}/compile?",
			new{ rootDoc_id = rootDoc, draft, check, incrementalCompilesEnabled = incremental, stopOnFirstError },
			ct);

		HttpStatusException.ThrowUnlessSuccessful(res, "trying to request compilation");

		return await getJson<Protocol.CompileInfo>(res.Content, ct);
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
	public async ValueTask<Protocol.JoinProjectArgs> GetInfo(bool cache = true, CancellationToken ct = default)
	{
		if(!cache || info is null)
		{
			await using var jp = await Join(ct);
			return await jp.GetProjectInfo(ct);
		}
		else
			return info;
	}

	/// <summary>
	///  Retrieves a file from a previously successful compilation
	/// </summary>
	/// <param name="f"> A file listed in the record returned by Compile() </param>
	/// <exception cref="HttpStatusException"> If the server returns any HTTP errors  </exception>
	/// <exception cref="TaskCanceledException"> If the configured HttpTimeout is exceeded </exception>
	public async Task<HttpContent> GetOutFile(Protocol.OutputFile f, CancellationToken ct = default)
	{
		var resp = await client.GetAsync($"project/{ID}/build/{f.Build}/output/{f.Path}", ct);

		HttpStatusException.ThrowUnlessSuccessful(resp, "trying to GET compilation result");

		return resp.Content;
	}

	/// <summary>
	///  Retrieves the history of updates.
	///  The return is sorted new to old.
	/// </summary>
	/// <remarks> Required read AND write permissions. A read-only share link will not work. </remarks>
	/// <exception cref="HttpStatusException"> If the server returns any HTTP errors </exception>
	/// <exception cref="HttpContentException"> If the server returns invalid JSON data </exception>
	/// <exception cref="HttpRequestException"> If an internal error occurs while making HTTP requests </exception>
	/// <exception cref="TaskCanceledException"> If the configured HttpTimeout is exceeded </exception>
	public async Task<Protocol.Update[]> GetUpdateHistory(CancellationToken ct = default)
	{
		var resp = await client.GetAsync($"project/{ID}/updates", ct);

		HttpStatusException.ThrowUnlessSuccessful(resp, "trying to GET update history");

		return (await getJson<Protocol.WrappedUpdates>(resp.Content, ct)).Updates;
	}

	/// <summary>
	///  Initializes a websocket instance for this project
	/// </summary>
	/// <exception cref="HttpStatusException"> If the server returns any HTTP errors </exception>
	/// <exception cref="HttpRequestException"> If an internal error occurs while making HTTP requests </exception>
	public Task<ProjectSession> Join(CancellationToken ct = default)
		=> ProjectSession.Connect(this, client, ct);

	/// <summary>
	///  The current state during log parsing
	/// </summary>
	private enum LogState
	{
		/// <summary> No special context </summary>
		INITIAL,
		/// <summary> Last line began with `!` </summary>
		GOT_BANG,
		/// <summary> last line was `<inserted text>`, or a subsequent indented line </summary>
		INSERTED_TEXT,
	}

	/// <summary>
	///  Attempts to retrieve the compiler warnings for a compilation.
	/// </summary>
	/// <returns> The extracted compiler warnings, if any. </returns>
	public async IAsyncEnumerable<string> ReadLogs(Protocol.CompileInfo compilation, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var log = compilation.OutputFiles.FirstOrDefault(f => f!.Path.EndsWith(".log"), null);

		if(log is null)
			yield break;

        using var data = await GetOutFile(log, ct);
        using var content = await data.ReadAsStreamAsync(ct);
		using var reader = new StreamReader(content);
		var cur = new StringBuilder();
		var state = LogState.INITIAL;

		for(; ; )
		{
			var line = await reader.ReadLineAsync(ct);

			if(line is null)
				yield break;

			line = line.TrimEnd();
			
			switch(state)
			{
				case LogState.INITIAL:
				{
					if(line.StartsWith("!"))
					{
						cur.Clear();
						cur.Append(line, 1, line.Length - 1);
						state = LogState.GOT_BANG;
					}
				}
				break;

				case LogState.GOT_BANG:
				finish_message:
				{
					if(line == "<inserted text>")
						state = LogState.INSERTED_TEXT;
					else if(line.Length == 0)
					{
						state = LogState.INITIAL;
						yield return cur.ToString();
					}
					else
					{
						cur.Append('\n');
						cur.Append(line);
					}
				}
				break;

				case LogState.INSERTED_TEXT:
				{
					if(line.Length == 0 || !char.IsWhiteSpace(line[0]))
						goto finish_message;
				}
				break;
			}
		}
	}
}
