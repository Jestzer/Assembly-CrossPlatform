using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssemblyAvalonia.Views;

public partial class AboutWindow : Window
{
	public AboutWindow()
	{
		InitializeComponent();

		var version = Assembly.GetExecutingAssembly().GetName().Version;
		string versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
		TitleText.Text = $"Assembly Crossplatform v{versionStr}";
	}

	private void OK_Click(object? sender, RoutedEventArgs e)
	{
		Close();
	}
}
