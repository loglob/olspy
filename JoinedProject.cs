using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Olspy.Util;

namespace Olspy;

/// <summary>
///  A session of the websocket API for a project
/// </summary>
public sealed class JoinedProject : IAsyncDisposable
{
	/// <summary>
	///  The joined project
	/// </summary>
	public readonly Project Project;

	// Use two cancellation tokens for a staggered close, since the websocket will get killed if any operation is cancelled
	private readonly CancellationTokenSource sendSource = new();
	private readonly CancellationTokenSource listenSource = new();
	private readonly Task listener;
	private readonly Task sender;
	private readonly WebSocket socket;
	private uint packetNumber = 0;

	private readonly SharedQueue<Message> sendQueue = new();
	private readonly Shared<Protocol.JoinProjectArgs> joinArgs = new();
	private readonly ConcurrentDictionary<uint, Shared<JsonNode?>> rpcResults = new();

	public bool Left
		=> socket.CloseStatus is not null;

	private JoinedProject(Project project, WebSocket socket)
	{
		this.Project = project;
		this.socket = socket;
		this.listener = listenLoop();
		this.sender = sendLoop();
	}

	internal static async Task<JoinedProject> Connect(Project project, HttpClient client)
	{
		var time = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
		var sock = await client.GetAsync($"socket.io/1/?projectId={project.ID}&t={time}");

		if(! sock.IsSuccessStatusCode)
			throw new Exception($"Got status {sock.StatusCode} trying to retrieve socket metadata for project");

		var cont = await sock.Content.ReadAsStringAsync();

		var key = cont.Split(':')[0];

		var wsc = new ClientWebSocket();

		await wsc.ConnectAsync(new Uri(client.BaseAddress!, $"socket.io/1/websocket/{key}?projectId={project.ID}").WithScheme("wss"), client, CancellationToken.None);

		return new JoinedProject(project, wsc);
	}

	/// <summary>
	///  Sends any messages in the `sendQueue` until the `sendSource` is cancelled.
	/// </summary>
	private async Task sendLoop()
	{
		try
		{
			for(;;)
			{
				var msg = await sendQueue.Dequeue(sendSource.Token);

				if(Left)
					break;

				// no token because cancelling this send would kill the socket and we need to send the close message
				await socket.SendAsync(msg.Data, msg.Type, true, default);
			}
		}
		finally
		{
			// does nothing but is observable in listenLoop()
			await sendSource.CancelAsync();
			// The listener will catch the response close handshake
			await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, null, CancellationToken.None);
		}
	}

	/// <summary>
	///  Listens for any websocket message until a close is received or the `listenSource` is cancelled.
	/// If an invalid message is received, initiate closing the session.
	/// </summary>
	private async Task listenLoop()
	{
		try
		{
			for(;;)
			{
				Message msg;

				// cancelling this receive *might* kill the socket, so the cancels are staggered
				try
				{
					msg = await socket.ReceiveCompleteAsync(listenSource.Token);				
				}
				catch(WebSocketException) when(sendSource.IsCancellationRequested)
				{
					// Closure doesn't work right and sometimes triggers
					// "The remote party closed the WebSocket connection without completing the close handshake"
					// not sure if it's my fault, .NET's or the Overleaf's
					// just ignore this exception since we're closing the socket anyways
					return;	
				}

				if(msg.Type == WebSocketMessageType.Close || Left)
					break;

				var dat = msg.Data;

				// TODO: nicer handling of invalid messages
				if(dat[1] != ':')
					throw new FormatException("Unexpected message data, expected 1 opcode byte");

				switch(dat[0])
				{
					case Protocol.INIT_REC:
					break;

					case Protocol.HEARTBEAT_REC:
						sendQueue.Enqueue(new Message(dat, WebSocketMessageType.Text));
					break;

					case Protocol.JOIN_PROJECT_REC:
					{
						if(dat[2] != ':' || dat[3] != ':')
							throw new FormatException("Invalid joinProjectResponse message");

						var cont = JsonNode.Parse(dat.Slice(4));

						if((string?) cont?["name"]?.AsValue() != "joinProjectResponse")
							throw new FormatException("Argument to join project opcode has invalid name");

						var arg = cont!["args"]!.AsArray()![0].Deserialize<Protocol.JoinProjectArgs>(Protocol.JsonOptions)!;
						await joinArgs.Write(arg, listenSource.Token);
					}
					break;

					case Protocol.RPC_RESULT_REC:
					{
						if(dat[2] != ':' || dat[3] != ':')
							throw new FormatException("Invalid joinProjectResponse message");

						int i;
						
						for (i = 0;;)
						{
							if(! char.IsDigit((char)dat[4 + i]))
								throw new FormatException("Expected packet number");

							if(dat[4 + ++i] == '+')
								break;
						}

						uint pNum = uint.Parse(dat.Slice(4, i));
						var data = JsonNode.Parse(dat.Slice(i + 5));

						if(rpcResults.TryRemove(pNum, out var sh))
							await sh.Write(data);
					}
					break;

					default:
						throw new FormatException($"Invalid opcode '{dat[0]}'");
				}
			}
		}
		finally
		{
			await sendSource.CancelAsync();
		}
	}

	/// <summary>
	///  Sends an RPC message, then awaits a response
	/// </summary>
	private async Task<JsonNode?> sendRPC(string kind, object[] args)
	{
		uint n = Interlocked.Increment(ref packetNumber);
		var obj = new { name = kind, args };

		// set up register to take result
		var res = new Shared<JsonNode?>();
		if(! rpcResults.TryAdd(n, res))
			throw new InvalidOperationException("Duplicate message number");

		var dat = $"{(char)Protocol.RPC_SEND}:{n}+::" + JsonSerializer.Serialize(obj);

		sendQueue.Enqueue(new( Encoding.UTF8.GetBytes(dat), WebSocketMessageType.Text ));

		var tick = new CancellationTokenSource(3000);
		return await res.Read(CancellationTokenSource.CreateLinkedTokenSource(tick.Token, listenSource.Token, sendSource.Token).Token);
	}


	public async ValueTask DisposeAsync()
	{
		if(!Left)
			await Leave();
	}

	/// <summary>
	///  Closes the websocket session
	/// </summary>
	/// <returns></returns>
	public async Task Leave()
	{
		await sendSource.CancelAsync();
		List<Exception> exs = [];

		try
		{
			await sender;
		}
		catch(OperationCanceledException)
		{}
		catch(Exception ex)
		{
			exs.Add(ex);
		}

		// The listener should close by itself after the closing message
		listenSource.CancelAfter(100);

		try
		{
			await listener;
		}
		catch(OperationCanceledException)
		{}
		catch(Exception ex)
		{
			exs.Add(ex);
		}

		socket.Dispose();

		if(exs.Count > 0)
			throw new AggregateException(exs);
	}

	/// <summary>
	///  Waits for the server-side join handshake to complete by sending project information 
	/// </summary>
	public async Task<Protocol.JoinProjectArgs> CompleteJoin(CancellationToken ct)
	{
		var lts = CancellationTokenSource.CreateLinkedTokenSource(listenSource.Token, ct);

		return await joinArgs.Read(lts.Token);
	}

	public async Task<Protocol.JoinProjectArgs> CompleteJoin()
		=> await joinArgs.Read(listenSource.Token);

	public async Task<string[]> GetDocumentByID(string ID)
	{
		var req = await sendRPC(Protocol.RPC_JOIN_DOCUMENT, [ ID, new{ encodeRanges = true } ]);
		await sendRPC(Protocol.RPC_LEAVE_DOCUMENT, [ ID ]);

		return req!.AsArray()![1]!.AsArray()!.Deserialize<string[]>()!;
	}
}