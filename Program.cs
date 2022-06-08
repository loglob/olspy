using Olspy.Interface;

namespace Olspy
{
	public static class Program
	{
		public static void Main(string[] args)
		{
			var o = Overleaf.RunningInstance;

			Console.WriteLine("Loaded overleaf instance at " + o.IP);
		}
	}
}