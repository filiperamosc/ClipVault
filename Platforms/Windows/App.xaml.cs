using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using ClipVault.Services;

namespace ClipVault.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	/// <summary>
	/// Configura nativamente o ícone da janela WinUI 3 via interop HWND e AppWindow.SetIcon.
	/// </summary>
	public static void ConfigureNativeWindowIcon(Microsoft.UI.Xaml.Window nativeWindow)
	{
		try
		{
			var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
			var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
			var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
			var iconPath = AppEnvironment.EnsureIconExtracted();

			ClipVault.App.ApplyWindowIcon(hWnd, appWindow, iconPath);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"Erro ao configurar ícone nativo da janela WinUI 3: {ex.Message}");
		}
	}
}

