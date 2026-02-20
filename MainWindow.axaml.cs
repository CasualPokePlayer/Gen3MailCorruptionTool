using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Gen3MailCorruptionTool;

public partial class MainWindow : Window
{
	private readonly MainViewModel _mainViewModel;

	public MainWindow()
	{
		InitializeComponent();
		_mainViewModel = new();
		DataContext = _mainViewModel;
	}

	public void ComputeCorruptionsHandler(object sender, RoutedEventArgs args)
	{
		_mainViewModel.ComputeCorruptions();
	}

	public void ResetHandler(object sender, RoutedEventArgs args)
	{
		_mainViewModel.Reset();
	}
}
