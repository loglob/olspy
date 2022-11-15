using System;
using Olspy;

namespace Olspy.Example
{
	public static class Program
	{
		/** A working example */
		private static async Task main()
		{
			// Search for a local overleaf instance
			var o = Overleaf.RunningInstance;
			Console.WriteLine("Loaded overleaf instance at " + o.IP);

			// check availability
			Console.WriteLine("Which is " + (await o.Available ? "alive" : "dead"));
			// open a project via an ID. You'll have to determine this ID manually
			var p = o.Open("62711f70548ace008c167cb9");
			// Set HTTP basic auth password (see README)
			o.SetCredentials(await File.ReadAllTextAsync("./token"));

			// Show project name, description, etc.
			Console.WriteLine(await p.GetProperties());

			// Enumerate all documents
			foreach (var d in await p.GetDocuments())
			{
				Console.WriteLine(d.ID+':');
				foreach (var l in d.Lines.Take(10))
					Console.WriteLine('\t'+l);
			}

			// Compile the project & store the resulting PDF
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