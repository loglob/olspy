using System;
using Olspy.Interface;

namespace Olspy
{
	public static class Program
	{
		private static async Task main()
		{
			var o = Overleaf.RunningInstance;

			Console.WriteLine("Loaded overleaf instance at " + o.IP);

			Console.WriteLine("Which is " + (await o.Available ? "alive" : "dead"));

			var p = o.Open("62711f70548ace008c167cb9");

			o.SetCredentials("test");

			Console.WriteLine(await p.GetProperties());

			using(var f = File.OpenWrite("temp.pdf"))
			using(var pdf = await p.Compile())
			{
				await pdf.CopyToAsync(f);
			}
		}

		public static void Main(string[] args)
			=> main().GetAwaiter().GetResult();
	}
}