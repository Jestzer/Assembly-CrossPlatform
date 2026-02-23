namespace AssemblyAvalonia.Models
{
	public class HeaderValue
	{
		public string Title { get; set; }
		public string Data { get; set; }

		public HeaderValue(string title, string data)
		{
			Title = title;
			Data = data;
		}
	}
}
