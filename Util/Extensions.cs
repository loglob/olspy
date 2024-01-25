using System.Net.WebSockets;
using System.Text;

namespace Olspy.Util;

/// <summary>
///  A complete websocket message
/// </summary>
public record struct Message( ArraySegment<byte> Data, WebSocketMessageType Type )
{
	public override readonly string ToString()
		=> Encoding.UTF8.GetString(Data);
}

public static class Extensions
{
	/// <summary>
	///  Receives a websocket message split over multiple packets
	/// </summary>
	/// <remarks>
	///  (!) When cancelled, the websocket is put into aborted state, blocking any subsequent method call.
	/// </remarks>
	public static async Task<Message> ReceiveCompleteAsync(this WebSocket ws, CancellationToken ct)
	{
		var chunk = new byte[10 * 1024];
		var complete = new List<byte>();

		for(;;)
		{
			var resp = await ws.ReceiveAsync(chunk, ct);

			complete.AddRange(new ReadOnlySpan<byte>(chunk, 0, resp.Count));

			if(resp.EndOfMessage)
				return new(complete.ToArray(), resp.MessageType);

			ct.ThrowIfCancellationRequested();
		}
	}

	public static Task<Message> ReceiveCompleteAsync(this WebSocket ws)
		=> ReceiveCompleteAsync(ws, CancellationToken.None);

	/// <returns> A new Uri that is identical to `old`, except for its scheme </returns>
	public static Uri WithScheme(this Uri old, string scheme)
	{
		var orig = old.OriginalString;
		int off = orig.IndexOf(Uri.SchemeDelimiter);

		return new Uri(scheme + ((off < 0) ? Uri.SchemeDelimiter + orig : orig[off..]));
	}

	/// <summary>
	///  Formats an array as a list enclosed in "[]"
	/// </summary>
	public static string Show<T>(this T[] arr)
	{
		if(arr.Length == 0)
			return "[]";

		var sb = new StringBuilder();
		sb.Append("[ ");

		for(int i = 0; i < arr.Length; i++)
		{
			if(i > 0)
				sb.Append(", ");

			sb.Append(arr[i]);
		}

		sb.Append(" ]");
		return sb.ToString();
	}

}