
using System.Diagnostics;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace Olspy.Interface
{
	/// <summary>
	/// An overleaf instance
	/// </summary>
	public class Overleaf
	{
		/// <summary>
		/// The IP of the overleaf server that also exposes internal APIs
		/// </summary>
		public readonly IPAddress IP;

		/// <summary>
		/// The HTTP client configuration used for API requests
		/// </summary>
		internal readonly HttpClient client = new HttpClient(){  };

		/// <summary>
		/// The port of the docstore service
		/// </summary>
		public ushort DocstorePort { get; init; } = 3016;

		/// <summary>
		/// The port of the filestore service
		/// </summary>
		public ushort FilestorePort { get; init; } = 3009;

		/// <summary>
		/// The port of the web frontend
		/// </summary>
		public ushort WebPort { get; init; } = 80;

		public Overleaf(IPAddress ip)
			=> this.IP = ip;

		/// <summary>
		/// Sets the credentials for accessing the internal overleaf API.
		/// </summary>
		/// <param name="password">Configured via WEB_API_PASSWORD in variables.env</param>
		/// <param name="username">Configured via WEB_API_USER in variables.env</param>
		public void SetCredentials(string password, string username = "sharelatex")
		{
			this.client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
				Convert.ToBase64String(ASCIIEncoding.UTF8.GetBytes($"{username}:{password}")));
		}

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

		/// <summary>
		/// Opens a project
		/// </summary>
		/// <param name="id">The unique identifier of the project</param>
		public Project Open(string id)
			=> new Project(this, id);

		/// <summary>
		/// Whether all services are available
		/// </summary>
		public Task<bool> Available
			=> Task
				.WhenAll(available(WebPort), available(DocstorePort), available(FilestorePort))
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