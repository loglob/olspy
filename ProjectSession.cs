using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Olspy.Util;

using static Olspy.Protocol;

namespace Olspy;

/// <summary>
///  A session of the websocket API for a project
/// </summary>
public sealed class ProjectSession : IAsyncDisposable
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

	private readonly AwaitableQueue<Message> sendQueue = new();
	private readonly WriteOnce<JoinProjectArgs> joinArgs = new();
	private readonly ConcurrentDictionary<uint, WriteOnce<JsonArray>> rpcResults = new();

	public bool Left
		=> socket.CloseStatus is not null;

	private ProjectSession(Project project, WebSocket socket)
	{
		this.Project = project;
		this.socket = socket;
		this.listener = listenLoop();
		this.sender = sendLoop();
	}

	internal static async Task<ProjectSession> Connect(Project project, HttpClient client)
	{
		var time = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
		var sock = await client.GetAsync($"socket.io/1/?projectId={project.ID}&t={time}");

		HttpStatusException.ThrowUnlessSuccessful(sock, "trying to retrieve socket metadata for project");

		var cont = await sock.Content.ReadAsStringAsync();

		var key = cont.Split(':')[0];

		var wsc = new ClientWebSocket();

		await wsc.ConnectAsync(new Uri(client.BaseAddress!, $"socket.io/1/websocket/{key}?projectId={project.ID}").WithScheme("wss"), client, CancellationToken.None);

		return new ProjectSession(project, wsc);
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
				catch(System.Net.WebSockets.WebSocketException) when(sendSource.IsCancellationRequested)
				{
					// Closure doesn't work right and sometimes triggers
					// "The remote party closed the WebSocket connection without completing the close handshake"
					// not sure if it's my fault, .NET's or the Overleaf's
					// just ignore this exception since we're closing the socket anyways
					return;
				}

				if(msg.Type == WebSocketMessageType.Close || Left)
					break;

				Packet pkt;

				try
				{
					pkt = Packet.Parse(msg.Data);
				}
				catch (Exception ex)
				{
					await Console.Error.WriteLineAsync("Received invalid packet: " + ex);
					continue;
				}

				try
				{
					switch(pkt.OpCode)
					{
						case OpCode.CONNECT:
						break;

						case OpCode.HEARTBEAT:
							sendQueue.Enqueue(new Message(msg.Data, WebSocketMessageType.Text));
						break;

						case OpCode.EVENT:
						{
							var (name, args) = pkt.EventPayload;

							// these events are send when other users edit the same documents
							if(name.StartsWith("clientTracking."))
								break;
							else if(name != RPC_JOIN_PROJECT)
								throw new NotImplementedException($"Unhandled server-side EVENT '{name}'");

							var v = args[0].Deserialize<JoinProjectArgs>(JsonOptions)!;

							Project.info = v;
							joinArgs.Write(v);
						}
						break;

						case OpCode.ACK:
						{
							var pNum = pkt.ID!.Value;
							var data = pkt.JsonPayload ?? throw new FormatException("Payload of ACK packet was null");

							if(rpcResults.TryRemove(pNum, out var sh))
								sh.Write(data is JsonArray a ? a : []);
						}
						break;

						case OpCode.DISCONNECT:
							return;

						default:
							throw new NotImplementedException($"Unhandled opcode: {pkt.OpCode}");
					}
				}
				catch(Exception ex)
				{
					await Console.Error.WriteLineAsync($"Could not process packet: " + ex);
					throw;
				}
			}
		}
		finally
		{
			// ensure this source is always cancelled when the listener won't process new packets
			if(! listenSource.IsCancellationRequested)
				await listenSource.CancelAsync();

			await sendSource.CancelAsync();
		}
	}

	/// <summary>
	///  Sends an RPC message, then awaits a response
	/// </summary>
	private async Task<JsonArray> sendRPC(string kind, object[] args)
	{
		uint n = Interlocked.Increment(ref packetNumber);
		var obj = new { name = kind, args };

		// set up register to take result
		var res = new WriteOnce<JsonArray>();
		if(! rpcResults.TryAdd(n, res))
			throw new InvalidOperationException("Duplicate message number");

		var dat = $"{(char)(OpCode.EVENT + '0')}:{n}+::" + JsonSerializer.Serialize(obj);

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
	///  Retrieves the project information
	///  Waits for the server-side join handshake to complete which sends project information.
	/// </summary>
	/// <exception cref="OperationCanceledException"> If the server's response isn't received before the timeout </exception>
	public async Task<Protocol.JoinProjectArgs> GetProjectInfo(CancellationToken ct)
	{
		var lts = CancellationTokenSource.CreateLinkedTokenSource(listenSource.Token, ct);

		return await joinArgs.Read(lts.Token);
	}

	public async Task<Protocol.JoinProjectArgs> GetProjectInfo()
		=> await joinArgs.Read(listenSource.Token);

	/// <summary>
	///  Resolves a document ID
	/// </summary>
	/// <param name="ID"> A file ID found in the project information </param>
	/// <returns> The lines of that document </returns>
	/// <exception cref="WebSocketException"> When the server returns an error message, e.g. when the file ID doesn't exist </exception>
	public async Task<string[]> GetDocumentByID(string ID)
	{
		var req = await sendRPC(RPC_JOIN_DOCUMENT, [ ID, new{ encodeRanges = true } ]);

		if(req[0] is not null)
			throw new WebSocketException("Failed document ID lookup", req[0]!);

		await sendRPC(RPC_LEAVE_DOCUMENT, [ ID ]);
		string[] lines;

		try
		{
			// TODO: figure out what the other entries do
			lines = req[1]!.AsArray()!.Deserialize<string[]>()!;
		}
		catch(Exception ex) when (ex is IndexOutOfRangeException or JsonException)
		{
			throw new WebSocketException("Invalid response format for joinDoc response", req, ex);
		}

		for (int i = 0; i < lines.Length; i++)
			lines[i] = Protocol.UnMangle(lines[i]);

		return lines;
	}
}