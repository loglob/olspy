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
	public readonly Project Project;
	// Use two cancellation tokens for a staggered close, since the websocket will get killed if any operation is cancelled
	private readonly CancellationTokenSource sendSource = new();
	private readonly CancellationTokenSource listenSource = new();
	private readonly Task listener;
	private readonly Task sender;
	private readonly WebSocket socket;

	private readonly SharedQueue<Message> sendQueue = new();
	private readonly Shared<Protocol.JoinProjectArgs> joinArgs = new();

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
			// The listener will catch the response close handshake
			await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, null, CancellationToken.None);
		}
	}

	private async Task listenLoop()
	{
		for(;;)
		{
			// cancelling this receive *might* kill the socket, so the cancels are staggered
			var msg = await socket.ReceiveCompleteAsync(listenSource.Token);

			if(msg.Type == WebSocketMessageType.Close || Left)
				break;

			var data = msg.Data;

			// TODO: nicer handling of invalid messages
			if(data.Count < 2 || data[1] != (byte)':')
				throw new FormatException("Unexpected message data, expected 1 opcode byte");

			switch(data[0])
			{
				case Protocol.INIT_REC:
				break;

				case Protocol.HEARTBEAT_REC:
					sendQueue.Enqueue(new Message(data, WebSocketMessageType.Text));
				break;

				case Protocol.JOIN_PROJECT_REC:
				{
					var spl = Encoding.UTF8.GetString(data).Split(':', 4);
					var cont = JsonNode.Parse(spl[^1]);

					if((string?) cont?["name"]?.AsValue() != "joinProjectResponse")
						throw new FormatException("Argument to join project opcode has invalid name");

					var arg = cont!["args"]!.AsArray()![0].Deserialize<Protocol.JoinProjectArgs>(Protocol.JsonOptions)!;
					await joinArgs.Write(arg, listenSource.Token);
				}
				break;

				default:
					throw new FormatException($"Invalid opcode '{data[0]}'");
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		if(!Left)
			await Leave();
	}
	
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

	public async Task<Protocol.JoinProjectArgs> CompleteJoin(CancellationToken ct)
	{
		var lts = CancellationTokenSource.CreateLinkedTokenSource(listenSource.Token, ct);

		return await joinArgs.Read(lts.Token);
	}

	public async Task<Protocol.JoinProjectArgs> CompleteJoin()
		=> await joinArgs.Read(listenSource.Token);

}