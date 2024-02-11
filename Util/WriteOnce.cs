
namespace Olspy.Util;

/// <summary>
///  A MRMW register that can only be set once.
///  Also allows mutex access to its contents.
/// </summary>
internal class WriteOnce<T> where T : class
{
	/// <summary>
	///  Set to 1 when a value becomes available.
	///  Set to 0 while an exclusive operation is run on `value`.
	/// </summary>
	private readonly SemaphoreSlim sem = new(0);
	private T? value = null;

	public WriteOnce(){}

	/// <summary>
	///  Overwrites the register's value
	/// </summary>
	/// <param name="x"> A non-null value to overwrite </param>
	/// <exception cref="ArgumentNullException">When `x` is `null`</exception>
	/// <exception cref="InvalidOperationException">When the register has already been overwritten</exception>
	public void Write(T x)
	{
		ArgumentNullException.ThrowIfNull(x);

		if(Interlocked.CompareExchange(ref value, x, null) != null)
			throw new InvalidOperationException("WriteOnce can only be set once");
		
		sem.Release();
	}

	/// <summary>
	///  Reads the register's value.
	///  Waits until `Write()` is used and any concurrent `Compute()` finish.
	/// </summary>
	public async Task<T> Read(CancellationToken ct)
	{
		await sem.WaitAsync(ct);
		var x = value!;
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
			return func(value!);		
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
			return await func(value!, ct);
		}
		finally
		{
			sem.Release();		
		}
	}

	public Task<S> ComputeAsync<S>(Func<T, Task<S>> func)
		=> ComputeAsync((x, _) => func(x), CancellationToken.None);
}