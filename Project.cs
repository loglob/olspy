namespace Olspy
{
	/// <summary>
	/// An overleaf project
	/// </summary>
	public class Project
	{
		/// <summary>
		/// Represents basic properties of a project
		/// </summary>
		/// <param name="Name">The project name</param>
		/// <param name="Description">The project description</param>
		/// <param name="Compiler">The compiler used by the project</param>
		public record Properties(string Name, string Description, string Compiler);

		/// <summary>
		/// The overleaf instance this project is hosted on
		/// </summary>
		public readonly Overleaf Instance;

		/// <summary>
		/// The unique identifier for this project
		/// </summary>
		public string ID { get; }

		internal Project(Overleaf instance, string id)
		{
			this.Instance = instance;
			this.ID = id;
		}

		/// <summary>
		/// Retrieves the basic properties of this project
		/// </summary>
		public async Task<Properties> GetProperties()
		{
			using(var res = await Instance.client.GetAsync($"http://{Instance.Host}:{Instance.WebPort}/internal/project/{ID}"))
			{
				return await res.ReadAsJsonAsync<Properties>();
			}
		}

		/// <summary>
		/// Retrieves a list of editable text documents in this project
		/// </summary>
		public async Task<Document[]> GetDocuments()
		{
			using(var res = await Instance.client.GetAsync($"http://{Instance.Host}:{Instance.DocstorePort}/project/{ID}/doc"))
			{
				return await res.ReadAsJsonAsync<Document[]>();
			}
		}

		/// <summary>
		/// Compiles a project (if needed) and returns a stream for the resulting PDF
		/// </summary>
		public async Task<Stream> Compile()
		{
			var res = await Instance.client.GetAsync($"http://{Instance.Host}:{Instance.WebPort}/internal/project/{ID}/compile/pdf");
			res.EnsureSuccessStatusCode();

			return await res.Content.ReadAsStreamAsync();
		}
	}
}