using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Olspy.Util;

namespace Olspy;

public class Project
{
	private const string SESSION_COOKIE = "sharelatex.sid";
	private static readonly Regex ReadWriteTokenPattern = new Regex("^[0-9]+[a-z]{6,12}$");
	private static readonly Regex ReadOnlyTokenPattern = new Regex("^[a-z]{12}$");

	public readonly string ID;
	private readonly HttpClient client;
	private readonly HttpClientHandler handler;


	private Project(string id, HttpClient client, HttpClientHandler handler)
	{
		this.ID = id;
		this.client = client;
		this.handler = handler;
	}

	private static bool validScheme(string scheme)
		=> scheme switch {
			"http" => true,
			"https" => true,
			_ => false
		};

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
		var reqC = await req.Content.ReadAsStringAsync();
		var csrf = new Regex("\"csrfToken\" *: *\"([a-zA-Z0-9=_-]+)\"").Match(reqC).Groups[1].Value;

		// if this isn't application/json, we get a 403
		var grant = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, shareLink.AbsolutePath + "/grant") {
			Content = new StringContent($"{{ \"_csrf\": \"{csrf}\", \"confirmedByUser\": false }}", new MediaTypeHeaderValue("application/json"))
		});
		
		if(! grant.IsSuccessStatusCode)
			throw new Exception($"Request for session grant did not succeed, got {grant.StatusCode}");

		var grantC = await grant.Content.ReadFromJsonAsync<Dictionary<string, string>>();

		if(grantC is null || !grantC.TryGetValue("redirect", out var red))
			throw new Exception("Join grant did not contain a project redirect URL");

		// note: I don't know what `red` is relative to if the server has some base prefix
		var id = new Regex("^/project/([0-9a-fA-F]+)$").Match(red).Groups[1].Value;
		
		return new Project(id, client, handler);
	}

	public static Task<Project> Open(string shareLink, WebProxy? proxy = null)
		=> Open(new Uri(shareLink), proxy);

	public static Project Open(Uri host, string ID, string session, WebProxy? proxy = null)
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

		return new Project(ID, client, handler);
	}
		
	public static Project Open(string host, string id, string sessionCookie, WebProxy? proxy = null)
		=> Open(new Uri(host), id, sessionCookie, proxy);

	private async Task<ClientWebSocket> connectSocket()
	{
		var time = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
		var sock = await client.GetAsync($"socket.io/1/?projectId={ID}&t={time}");

		if(! sock.IsSuccessStatusCode)
			throw new Exception($"Got status {sock.StatusCode} trying to retrieve socket metadata for project");

		var cont = await sock.Content.ReadAsStringAsync();

		var key = cont.Split(':')[0];

		var wsc = new ClientWebSocket();


		await wsc.ConnectAsync(new Uri(client.BaseAddress!, $"socket.io/1/websocket/{key}?projectId={ID}").WithScheme("wss"), client, CancellationToken.None);
		var buf = new byte[3];
		var rec = await wsc.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);

		if(!rec.EndOfMessage || rec.Count != 3 || !Enumerable.SequenceEqual(buf, new[]{ (byte)'1', (byte)':', (byte)':' }))
			throw new Exception($"Invalid handshake message, expected \"1::\"");
		if(rec.CloseStatus is not null)
			throw new Exception("WebSocket closed immediately");

		return wsc;
	}

	public Task<JoinedProject> Join()
		=> JoinedProject.Connect(this, client);

	public async Task<Protocol.JoinProjectArgs> GetInfo()
	{
		await using var jp = await Join();

		return await jp.CompleteJoin();
	}
}
