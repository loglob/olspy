
using System.Diagnostics;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Olspy.Interface
{
	public class Overleaf
	{
		/// <summary>
		/// The IP of the overleaf server that also exposes internal APIs
		/// </summary>
		public readonly IPAddress IP;

		/// <summary>
		/// The HTTP client configuration used for API requests
		/// </summary>
		internal readonly HttpClient client = new HttpClient(){ Timeout = TimeSpan.FromMilliseconds(500) };

		public ushort DocstorePort { get; init; } = 3016;
		public ushort FileStorePort { get; init; } = 3009;

		public ushort WebPort { get; init; } = 80;

		public Overleaf(IPAddress ip)
			=> this.IP = ip;

		/// <summary>
		/// Tests for availability of the given service
		/// </summary>
		/// <returns>Whether the port is open and reports availability</returns>
		private async Task<bool> available(ushort port)
		{
			using(var response = await client.GetAsync($"http://{IP}:{port}/status"))
			{
				return response.IsSuccessStatusCode;
			}
		}

		public Project Open(string uuid)
			=> new Project(this, uuid);

		public Task<bool> Available
			=> Task
				.WhenAll(available(WebPort), available(DocstorePort), available(FileStorePort))
				.Map(Util.All);

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
					.SelectWhere<string, IPAddress>(IPAddress.TryParse)
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