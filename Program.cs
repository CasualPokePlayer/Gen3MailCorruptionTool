using System;

using Avalonia;

namespace Gen3MailCorruptionTool;

internal static class Program
{
	public static AppBuilder BuildAvaloniaApp() 
		=> AppBuilder.Configure<App>().UsePlatformDetect();

	[STAThread]
	private static void Main(string[] args)
		=> BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
