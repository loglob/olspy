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

			var p = o.Open("61f097e5d787020085e21ad8");

			foreach(var f in await p.GetFiles())
			{
				Console.WriteLine($"{f.ID}: {f.Lines[0]}");
			}
		}

		public static void Main(string[] args)
			=> main().GetAwaiter().GetResult();
	}
}