using Blamite.Serialization;

namespace AssemblyAvalonia.Helpers
{
	/// <summary>
	///     Global application state — replaces WPF's App.AssemblyStorage pattern.
	/// </summary>
	public static class AppState
	{
		public static AppSettings Settings { get; set; } = AppSettings.Load();
		public static EngineDatabase EngineDb { get; set; }
	}
}
