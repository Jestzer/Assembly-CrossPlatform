using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssemblyAvalonia.Views;

public partial class ErrorDialog : Window
{
	public ErrorDialog()
	{
		InitializeComponent();
	}

	public ErrorDialog(string message) : this()
	{
		MessageText.Text = message;
	}

	private void OK_Click(object? sender, RoutedEventArgs e)
	{
		Close();
	}

	public static Task Show(Window owner, string message)
	{
		var dialog = new ErrorDialog(message);
		return dialog.ShowDialog(owner);
	}
}
