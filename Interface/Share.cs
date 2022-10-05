using System.Xml;
using HtmlAgilityPack;

namespace Olspy.Interface
{
	/// <summary>
	/// Represents a public shared document
	/// </summary>
	public class Share
	{
		public readonly Overleaf Instance;
		public readonly string Token;

		public Share(Overleaf instance, string token)
		{
			this.Instance = instance;
			this.Token = token;
		}

		public async Task open()
		{
			using(var res = await Instance.client.GetAsync($"http://{Instance.IP}:{Instance.WebPort}/{Token}"))
			using(var s = await res.Content.ReadAsStreamAsync())
			using(var x = new XmlTextWriter(Console.Out){ Formatting = Formatting.Indented })
			{
				var doc = new HtmlDocument();
				doc.Load(s);

				doc.Save(x);
			}
		}
	}
}