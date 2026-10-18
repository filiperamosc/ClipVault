using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClipVault.Services;
using ClipVault.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace ClipVault;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Debug.WriteLine($"Exceção global capturada: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Debug.WriteLine($"Exceção de Task em background observada: {e.Exception.Message}");
            e.SetObserved();
        };

#if WINDOWS
        if (Microsoft.UI.Xaml.Application.Current != null)
        {
            Microsoft.UI.Xaml.Application.Current.UnhandledException += (s, e) =>
            {
                Debug.WriteLine($"Exceção WinUI capturada e tratada: {e.Message}");
                e.Handled = true;
            };
        }
#endif

        MainPage = new AppShell();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        window.Title = "ClipVault";
        window.MinimumWidth = 600;
        window.MinimumHeight = 500;
        window.Width = 880;
        window.Height = 660;

#if WINDOWS
        window.Created += (s, e) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                try
                {
                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                    var iconPath = AppEnvironment.EnsureIconExtracted();

                    ApplyWindowIcon(hWnd, appWindow, iconPath);
                    nativeWindow.Activated += (sender, args) =>
                    {
                        ApplyWindowIcon(hWnd, appWindow, iconPath);
                    };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao definir ícone da janela nativa: {ex.Message}");
                }

                var trayService = _serviceProvider.GetRequiredService<ITrayService>();
                trayService.Initialize(nativeWindow);

                trayService.RequestShowWindow += () =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        trayService.ShowWindow();
                    });
                };

                trayService.RequestSettingsWindow += () =>
                {
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        trayService.ShowWindow();
                        if (MainPage is Shell shell)
                        {
                            var settingsPage = _serviceProvider.GetRequiredService<SettingsPage>();
                            await shell.Navigation.PushAsync(settingsPage);
                        }
                    });
                };

                trayService.RequestAboutWindow += () =>
                {
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        trayService.ShowWindow();
                        if (MainPage is Shell shell)
                        {
                            var aboutPage = _serviceProvider.GetRequiredService<AboutPage>();
                            await shell.Navigation.PushAsync(aboutPage);
                        }
                    });
                };

                trayService.RequestExit += () =>
                {
                    try
                    {
                        var clipboard = _serviceProvider.GetService<IClipboardService>();
                        clipboard?.Dispose();
                        var autoClean = _serviceProvider.GetService<IAutoCleanService>();
                        (autoClean as IDisposable)?.Dispose();
                        var db = _serviceProvider.GetService<IDatabaseService>();
                        db?.Dispose();
                    }
                    catch { }
                };
            }

            var autoCleanService = _serviceProvider.GetRequiredService<IAutoCleanService>();
            autoCleanService.StartPeriodicCleaning();
        };
#endif

        return window;
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetClassLong")]
    private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetClassLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return SetClassLong32(hWnd, nIndex, dwNewLong);
    }

    private const uint WM_SETICON = 0x0080;
    private const IntPtr ICON_SMALL = 0;
    private const IntPtr ICON_BIG = (IntPtr)1;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    public static void ApplyWindowIcon(IntPtr hWnd, Microsoft.UI.Windowing.AppWindow? appWindow, string iconPath)
    {
        if (string.IsNullOrEmpty(iconPath) || !System.IO.File.Exists(iconPath))
            return;

        string absolutePath = System.IO.Path.GetFullPath(iconPath);

        // 1. Invocação primária direta de AppWindow.SetIcon com o caminho absoluto do .ico
        try
        {
            if (appWindow != null)
            {
                appWindow.SetIcon(absolutePath);

                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && appWindow.TitleBar != null)
                {
                    appWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.ShowIconAndSystemMenu;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppWindow.SetIcon(path) erro: {ex.Message}");
        }

        // 2. Interop Win32 via LoadImage para obter HICON nativo e aplicar via IconId e WM_SETICON
        try
        {
            if (hWnd != IntPtr.Zero)
            {
                var hSmall = LoadImage(IntPtr.Zero, absolutePath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                if (hSmall != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_SETICON, ICON_SMALL, hSmall);
                    SetClassLongPtr(hWnd, GCLP_HICONSM, hSmall);

                    if (appWindow != null)
                    {
                        try
                        {
                            var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hSmall);
                            appWindow.SetIcon(iconId);
                        }
                        catch { }
                    }
                }

                var hBig = LoadImage(IntPtr.Zero, absolutePath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                if (hBig != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_SETICON, ICON_BIG, hBig);
                    SetClassLongPtr(hWnd, GCLP_HICON, hBig);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Win32 WM_SETICON erro: {ex.Message}");
        }
    }
#endif
}
