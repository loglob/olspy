using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Olspy.Interface
{
	public class Project
	{
		/// <summary>
		/// The overleaf instance this project is hosted on
		/// </summary>
		protected Overleaf instance;

		public string UUID { get; }

		internal Project(Overleaf instance, string uuid)
		{
			this.instance = instance;
			this.UUID = uuid;
		}

		public async Task<Document[]> GetFiles()
		{
			using(var res = await instance.client.GetAsync($"http://{instance.IP}:{instance.DocstorePort}/project/{UUID}/doc"))
			using(var s = await res.Content.ReadAsStreamAsync())
			using(var r = new StreamReader(s))
			using(var t = new JsonTextReader(r))
			{
				return new JsonSerializer().Deserialize<Document[]>(t);
			}
		}
	}
}