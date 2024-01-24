
namespace Olspy.Util;

/// <summary>
///  A MRMW register
/// </summary>
public class Shared<T>
{
	private SemaphoreSlim sem = new(0);
	uint present = 0;
#pragma warning disable 8601
	private T value = default;
#pragma warning restore

	public Shared(){}

	public async ValueTask Write(T x, CancellationToken ct)
	{
		if(Interlocked.CompareExchange(ref present, 1, 0) == 0)
		{
			value = x;
			sem.Release();
		}
		else
		{
			await sem.WaitAsync(ct);
			value = x;
			sem.Release();
		}
	}

	public ValueTask Write(T x)
		=> Write(x, CancellationToken.None);

	public async Task<T> Read(CancellationToken ct)
	{
		await sem.WaitAsync(ct);
		var x = value;
		sem.Release();

		return x;
	}

	public Task<T> Read()
		=> Read(CancellationToken.None);

	/// <summary>
	///  Runs a computation that has requires exclusive access to the shared resource
	/// </summary>
	public async Task<S> Compute<S>(Func<T, S> func, CancellationToken ct)
	{
		await sem.WaitAsync(ct);
		try
		{
			return func(value);		
		}
		finally
		{
			sem.Release();		
		}
	}

	public Task<S> Compute<S>(Func<T, S> func)
		=> Compute(func, CancellationToken.None);

	public async Task<S> ComputeAsync<S>(Func<T, CancellationToken, Task<S>> func, CancellationToken ct)
	{
		await sem.WaitAsync(ct);
		try
		{
			return await func(value, ct);
		}
		finally
		{
			sem.Release();		
		}
	}

	public Task<S> ComputeAsync<S>(Func<T, Task<S>> func)
		=> ComputeAsync((x, _) => func(x), CancellationToken.None);
}