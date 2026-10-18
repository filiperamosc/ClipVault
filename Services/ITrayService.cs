using System;

namespace ClipVault.Services;

public interface ITrayService
{
    event Action? RequestShowWindow;
    event Action? RequestSettingsWindow;
    event Action? RequestClearHistory;
    event Action? RequestToggleMonitoring;
    event Action? RequestAboutWindow;
    event Action? RequestExit;

    void Initialize(object nativeWindow);
    void ShowWindow();
    void HideWindow();
    void UpdateStatus(bool isPaused);
    void Shutdown();
}
