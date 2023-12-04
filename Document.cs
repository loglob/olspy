
using Newtonsoft.Json;

namespace Olspy
{
	public class Document
	{
		[JsonProperty("_id")]
		public string ID { get; }

		public string[] Lines { get; }

		public int Revision { get; }

		[JsonConstructor]
		internal Document(string _id, string[] lines, int rev)
		{
			this.ID = _id;
			this.Lines = lines;
			this.Revision = rev;
		}
	}
}
