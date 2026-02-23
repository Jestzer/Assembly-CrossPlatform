using System.IO;
using System.Text.RegularExpressions;

namespace AssemblyAvalonia.Helpers
{
	public static class VariousFunctions
	{
		/// <summary>
		///     Replaces characters that are invalid in file names with underscores.
		/// </summary>
		public static string SterilizeTagGroupName(string tagGroupName)
		{
			string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
			string pattern = string.Format(@"(\.+$)|([{0}])", invalidChars);
			return Regex.Replace(tagGroupName, pattern, "_");
		}
	}
}
