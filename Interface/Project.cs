namespace Olspy.Interface
{
	public class Project
	{
		public record Properties(string Name, string Description, string Compiler);

		/// <summary>
		/// The overleaf instance this project is hosted on
		/// </summary>
		protected Overleaf instance;

		public string ID { get; }

		internal Project(Overleaf instance, string id)
		{
			this.instance = instance;
			this.ID = id;
		}

		/// <summary>
		/// Retrieves the basic properties of this project
		/// </summary>
		public async Task<Properties> GetProperties()
		{
			using(var res = await instance.client.GetAsync($"http://{instance.IP}:{instance.WebPort}/internal/project/{ID}"))
			{
				return await res.ReadAsJsonAsync<Properties>();
			}
		}

		/// <summary>
		/// Retrieves a list of editable text documents in this project
		/// </summary>
		public async Task<Document[]> GetDocuments()
		{
			using(var res = await instance.client.GetAsync($"http://{instance.IP}:{instance.DocstorePort}/project/{ID}/doc"))
			{
				return await res.ReadAsJsonAsync<Document[]>();
			}
		}

		/// <summary>
		/// Compiles a project (if needed) and returns a stream for the resulting PDF
		/// </summary>
		public async Task<Stream> Compile()
		{
			var res = await instance.client.GetAsync($"http://{instance.IP}:{instance.WebPort}/internal/project/{ID}/compile/pdf");
			res.EnsureSuccessStatusCode();

			return await res.Content.ReadAsStreamAsync();
		}
	}
}