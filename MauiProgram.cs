using ClipVault.Services;
using ClipVault.ViewModels;
using ClipVault.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

using Microsoft.Maui.LifecycleEvents;

namespace ClipVault;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if WINDOWS
        Microsoft.Maui.Controls.Handlers.ShellHandler.Mapper.AppendToMapping("SuppressWinUINavControls", (handler, view) =>
        {
            if (handler.PlatformView is Microsoft.UI.Xaml.Controls.NavigationView navView)
            {
                navView.IsBackButtonVisible = Microsoft.UI.Xaml.Controls.NavigationViewBackButtonVisible.Collapsed;
                navView.IsTitleBarAutoPaddingEnabled = false;
                navView.IsPaneToggleButtonVisible = false;
                navView.Header = null;
                navView.AlwaysShowHeader = false;
                navView.PaneDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.LeftMinimal;
            }
        });

        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(windows => windows
                .OnWindowCreated(nativeWindow =>
                {
                    try
                    {
                        ClipVault.WinUI.App.ConfigureNativeWindowIcon(nativeWindow);
                        if (App.Current is App app)
                        {
                            app.InitializeNative(nativeWindow);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Erro no ciclo de vida da janela: {ex.Message}");
                    }
                }));
        });
#endif

        // Registro de Serviços (Singletons)
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IStorageService, StorageService>();
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
        builder.Services.AddSingleton<IOcrService, WindowsOcrService>();
        builder.Services.AddSingleton<IVisionClassifierService, VisionClassifierService>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<ITrayService, TrayService>();
        builder.Services.AddSingleton<IAutoCleanService, AutoCleanService>();

        // Registro de ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Registro de Páginas
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AboutPage>();
        builder.Services.AddTransient<ItemViewerPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
