using System;
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
}
