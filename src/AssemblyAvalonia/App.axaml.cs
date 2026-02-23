using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AssemblyAvalonia.Helpers;
using AssemblyAvalonia.Views;

namespace AssemblyAvalonia;

public partial class App : Application
{
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			var window = new MainWindow();

			// Restore window state from settings
			var settings = AppState.Settings;
			window.Width = settings.WindowWidth;
			window.Height = settings.WindowHeight;
			if (settings.WindowMaximized)
				window.WindowState = Avalonia.Controls.WindowState.Maximized;

			desktop.MainWindow = window;
			desktop.ShutdownRequested += (_, _) =>
			{
				// Save window state on exit
				settings.WindowWidth = window.Width;
				settings.WindowHeight = window.Height;
				settings.WindowMaximized = window.WindowState == Avalonia.Controls.WindowState.Maximized;
				settings.Save();
			};
		}

		base.OnFrameworkInitializationCompleted();
	}
}
