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
        window.HandlerChanged += (s, e) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                InitializeNative(nativeWindow);
            }
        };

        window.Created += (s, e) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                InitializeNative(nativeWindow);
            }
        };
#endif

        return window;
    }

#if WINDOWS
    private bool _isNativeInitialized;
    private readonly object _nativeInitLock = new();

    public void InitializeNative(Microsoft.UI.Xaml.Window nativeWindow)
    {
        lock (_nativeInitLock)
        {
            if (_isNativeInitialized) return;
            _isNativeInitialized = true;
        }

        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var iconPath = AppEnvironment.EnsureIconExtracted();

            ConfigureNativeTitleBar(nativeWindow, appWindow);
            ApplyWindowIcon(hWnd, appWindow, iconPath);
            int activationReapplyCount = 0;
            nativeWindow.Activated += (sender, args) =>
            {
                // O MAUI pode reativar a barra customizada (sem ícone) ao concluir a conexão da raiz de navegação,
                // o que ocorre nas primeiras ativações. Depois disso não há mais nada a reaplicar.
                if (activationReapplyCount >= 3) return;
                activationReapplyCount++;
                try
                {
                    ConfigureNativeTitleBar(nativeWindow, appWindow);
                    ApplyWindowIcon(hWnd, appWindow, iconPath);
                }
                catch { }
            };

            var trayService = _serviceProvider.GetRequiredService<ITrayService>();
            trayService.Initialize(nativeWindow);

            trayService.RequestShowWindow += () =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        trayService.ShowWindow();
                    }
                    catch { }
                });
            };

            trayService.RequestSettingsWindow += () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        trayService.ShowWindow();
                        if (MainPage is Shell shell)
                        {
                            var settingsPage = _serviceProvider.GetRequiredService<SettingsPage>();
                            await shell.Navigation.PushAsync(settingsPage);
                        }
                    }
                    catch { }
                });
            };

            trayService.RequestAboutWindow += () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        trayService.ShowWindow();
                        if (MainPage is Shell shell)
                        {
                            var aboutPage = _serviceProvider.GetRequiredService<AboutPage>();
                            await shell.Navigation.PushAsync(aboutPage);
                        }
                    }
                    catch { }
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

            var autoCleanService = _serviceProvider.GetRequiredService<IAutoCleanService>();
            autoCleanService.StartPeriodicCleaning();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao inicializar janela nativa e bandeja: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hinst, IntPtr uId, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetClassLongW")]
    private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8 ? SetClassLongPtr64(hWnd, nIndex, dwNewLong) : SetClassLong32(hWnd, nIndex, dwNewLong);

    private const uint WM_SETICON = 0x0080;
    private static readonly IntPtr ICON_SMALL = IntPtr.Zero;
    private static readonly IntPtr ICON_BIG = (IntPtr)1;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const int SM_CXICON = 11;
    private const int SM_CXSMICON = 49;

    // HICONs são carregados uma única vez por DPI. Antes, cada ativação da janela criava novos
    // handles via LoadImage sem DestroyIcon (vazamento de objetos GDI/USER).
    private static readonly object _iconHandleLock = new();
    private static uint _cachedIconDpi;
    private static IntPtr _cachedSmallIcon;
    private static IntPtr _cachedBigIcon;

    private static IntPtr LoadIconHandle(string? path, int size)
    {
        IntPtr h = IntPtr.Zero;
        if (!string.IsNullOrEmpty(path))
            h = LoadImage(IntPtr.Zero, path, IMAGE_ICON, size, size, LR_LOADFROMFILE);

        if (h == IntPtr.Zero)
        {
            // Fallback: recurso Win32 do executável (<ApplicationIcon>, ID 32512 gerado pelo SDK ou ordinal 1)
            var hModule = GetModuleHandle(null);
            if (hModule != IntPtr.Zero)
            {
                h = LoadImage(hModule, (IntPtr)32512, IMAGE_ICON, size, size, 0);
                if (h == IntPtr.Zero) h = LoadImage(hModule, (IntPtr)1, IMAGE_ICON, size, size, 0);
            }
        }
        return h;
    }

    private static void ConfigureNativeTitleBar(Microsoft.UI.Xaml.Window nativeWindow, Microsoft.UI.Windowing.AppWindow? appWindow)
    {
        try
        {
            // A barra de título customizada do MAUI (conteúdo estendido) não renderiza ícone algum.
            // Devolvemos a legenda ao DWM, que desenha o ícone definido via WM_SETICON/AppWindow.SetIcon.
            if (nativeWindow.ExtendsContentIntoTitleBar)
                nativeWindow.ExtendsContentIntoTitleBar = false;

            if (appWindow == null || !Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                return;

            var tb = appWindow.TitleBar;
            if (tb.ExtendsContentIntoTitleBar)
                tb.ExtendsContentIntoTitleBar = false;
            tb.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.ShowIconAndSystemMenu;

            var bg = Microsoft.UI.ColorHelper.FromArgb(255, 0x0F, 0x0F, 0x12);
            var fg = Microsoft.UI.ColorHelper.FromArgb(255, 0xE4, 0xE4, 0xE7);
            var inactiveFg = Microsoft.UI.ColorHelper.FromArgb(255, 0x71, 0x71, 0x7A);
            tb.BackgroundColor = bg;
            tb.ForegroundColor = fg;
            tb.InactiveBackgroundColor = bg;
            tb.InactiveForegroundColor = inactiveFg;
            tb.ButtonBackgroundColor = bg;
            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveBackgroundColor = bg;
            tb.ButtonInactiveForegroundColor = inactiveFg;
            tb.ButtonHoverBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 0x23, 0x23, 0x2B);
            tb.ButtonHoverForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 0xFF, 0xFF, 0xFF);
            tb.ButtonPressedBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 0x2E, 0x2E, 0x38);
            tb.ButtonPressedForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 0xFF, 0xFF, 0xFF);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Erro ao configurar barra de título nativa: {ex.Message}");
        }
    }

    public static void ApplyWindowIcon(IntPtr hWnd, Microsoft.UI.Windowing.AppWindow? appWindow, string iconPath)
    {
        string? absolutePath = null;
        try
        {
            if (!string.IsNullOrEmpty(iconPath) && System.IO.File.Exists(iconPath))
                absolutePath = System.IO.Path.GetFullPath(iconPath);
        }
        catch { }

        // 1. AppWindow.SetIcon com o .ico multirresolução (barra de título + barra de tarefas + Alt+Tab)
        if (appWindow != null && absolutePath != null)
        {
            try { appWindow.SetIcon(absolutePath); }
            catch (Exception ex) { Debug.WriteLine($"AppWindow.SetIcon(path) erro: {ex.Message}"); }
        }

        // 2. Win32: WM_SETICON + ícone de classe, com handles dimensionados para o DPI atual
        if (hWnd == IntPtr.Zero) return;
        try
        {
            IntPtr hSmall, hBig;
            lock (_iconHandleLock)
            {
                uint dpi = 96;
                try { dpi = GetDpiForWindow(hWnd); if (dpi == 0) dpi = 96; } catch { }

                if (_cachedIconDpi != dpi || _cachedSmallIcon == IntPtr.Zero || _cachedBigIcon == IntPtr.Zero)
                {
                    int small = 16, big = 32;
                    try
                    {
                        small = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
                        big = GetSystemMetricsForDpi(SM_CXICON, dpi);
                    }
                    catch { }

                    var newSmall = LoadIconHandle(absolutePath, small > 0 ? small : 16);
                    var newBig = LoadIconHandle(absolutePath, big > 0 ? big : 32);
                    // Handles antigos permanecem válidos (podem estar associados à janela); só são substituídos em mudança de DPI.
                    if (newSmall != IntPtr.Zero) _cachedSmallIcon = newSmall;
                    if (newBig != IntPtr.Zero) _cachedBigIcon = newBig;
                    _cachedIconDpi = dpi;
                }
                hSmall = _cachedSmallIcon;
                hBig = _cachedBigIcon;
            }

            if (hSmall != IntPtr.Zero)
            {
                SendMessage(hWnd, WM_SETICON, ICON_SMALL, hSmall);
                SetClassLongPtr(hWnd, GCLP_HICONSM, hSmall);
            }
            if (hBig != IntPtr.Zero)
            {
                SendMessage(hWnd, WM_SETICON, ICON_BIG, hBig);
                SetClassLongPtr(hWnd, GCLP_HICON, hBig);
            }

            if (appWindow != null && absolutePath == null && hBig != IntPtr.Zero)
            {
                try { appWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(hBig)); } catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Win32 WM_SETICON erro: {ex.Message}");
        }
    }
#endif
}
