using Newtonsoft.Json;

namespace Olspy.Interface
{
	internal static class Util
	{
		public delegate bool MaybeFunc<A, B>(A input, out B output);

		public static IEnumerable<B> SelectWhere<A,B>(this IEnumerable<A> input, MaybeFunc<A,B> f)
		{
			foreach (var i in input)
			{
				if(f(i, out var b))
					yield return b;
			}
		}

		public static IEnumerable<T> DeNull<T>(this IEnumerable<T?> inputs) where T : class
		{
			foreach(var i in inputs)
			{
				if(i is T x)
					yield return x;
			}
		}

		public static IEnumerable<T> TryCast<T>(this IEnumerable<object> items)
		{
			foreach (var i in items)
			{
				if(i is T x)
					yield return x;
			}
		}

		public static async Task<B> Map<A, B>(this Task<A> t, Func<A, B> f)
			=> f(await t);

		public static bool All(this IEnumerable<bool> bs)
			=> bs.All(x => x);

		public static async Task<T> ReadAsJsonAsync<T>(this HttpResponseMessage res)
		{
			res.EnsureSuccessStatusCode();

			using(var s = await res.Content.ReadAsStreamAsync())
			using(var r = new StreamReader(s))
			using(var t = new JsonTextReader(r))
			{
				return new JsonSerializer().Deserialize<T>(t);
			}
		}
	}
}