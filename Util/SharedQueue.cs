using System.Collections.Concurrent;

namespace Olspy.Util;

/// <summary>
///  A MRMW queue where Dequeue can be await'ed 
/// </summary>
public class SharedQueue<T>
{
	private readonly SemaphoreSlim count = new(0);
	/// <summary>
	///  Contains at least `count` items.
	///  Eventually contains exactly `count` items.
	/// </summary>
	private readonly ConcurrentQueue<T> items = new();

	public SharedQueue(){}

	public int Count
		=> count.CurrentCount;

	public void Enqueue(T x)
	{
		items.Enqueue(x);
		count.Release();
	}

	public async Task<T> Dequeue(CancellationToken ct)
	{
		await count.WaitAsync(ct);
		
		if(! items.TryDequeue(out var x))
			// this is impossible
			throw new InvalidOperationException("Semaphore and queue out of sync");

		return x;
	}

	public Task<T> Dequeue()
		=> Dequeue(CancellationToken.None);
}