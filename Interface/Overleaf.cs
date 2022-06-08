using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Web;
using System.Net;

namespace Olspy.Interface
{
	public class Overleaf
	{
		public readonly IPAddress IP;

		public ushort DocstorePort { get; init; } = 3016;
		public ushort FileStorePort { get; init; } = 3009;

		public Overleaf(IPAddress ip)
			=> this.IP = ip;

		/// <summary>
		/// Retrieves all configured IPs of a running docker container
		/// </summary>
		/// <param name="service">The docker container name</param>
		/// <returns>Every IPAddress that container is reachable under</returns>
		internal static List<IPAddress> GetIPs(string service)
		{
			var p = new Process()
			{
				StartInfo = new ProcessStartInfo()
				{
					RedirectStandardOutput = true,
					FileName = "docker",
					Arguments = $"container inspect {service}"
				}
			};

			p.Start();
			List<IPAddress> ips;

			using(var t = new JsonTextReader(p.StandardOutput))
			{
				ips = JObject.ReadFrom(t)
					.SelectTokens("..IPAddress")
					.TryCast<JValue>()
					.Select(v => v.Value)
					.TryCast<string>()
					.SelectWhere<string, IPAddress?>(IPAddress.TryParse)
					.DeNull()
					.ToList();
			}

			p.WaitForExit();

			return ips;
		}

		/// <summary>
		/// Finds the currently running overleaf container
		/// </summary>
		public static Overleaf RunningInstance
		{
			get
			{
				var ips = GetIPs("sharelatex");

				if(ips.Count == 0)
					throw new Exception("No active overleaf instance detected");
				else if(ips.Count > 1)
					throw new Exception("Multiple overleaf configurations detected");
				else
					return new Overleaf(ips[0]);
			}
		}
	}
}