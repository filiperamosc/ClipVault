using System;
using System.Runtime.InteropServices;
using ClipVault.Services;

#if WINDOWS
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
#endif

namespace ClipVault.Services;

public class TrayService : ITrayService
{
    public event Action? RequestShowWindow;
    public event Action? RequestSettingsWindow;
    public event Action? RequestClearHistory;
    public event Action? RequestToggleMonitoring;
    public event Action? RequestExit;

    private bool _isPaused;

#if WINDOWS
    private IntPtr _hwnd;
    private AppWindow? _appWindow;
    private bool _isSubclassed;
    private SubclassProc? _subclassProcDelegate;

    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 101;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_LBUTTONUP = 0x0202;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const int CMD_OPEN = 1001;
    private const int CMD_PAUSE = 1002;
    private const int CMD_CLEAR = 1003;
    private const int CMD_SETTINGS = 1004;
    private const int CMD_EXIT = 1005;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private NOTIFYICONDATAW _nid;
#endif

    public void Initialize(object nativeWindow)
    {
#if WINDOWS
        if (nativeWindow is Microsoft.UI.Xaml.Window winuiWindow)
        {
            _hwnd = WindowNative.GetWindowHandle(winuiWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            // Interceptar fechar (X) para apenas ocultar
            if (_appWindow != null)
            {
                _appWindow.Closing += (sender, args) =>
                {
                    args.Cancel = true;
                    _appWindow.Hide();
                };
            }

            // Criar ícone da bandeja
            IntPtr hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION padrão

            _nid = new NOTIFYICONDATAW
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = hIcon,
                szTip = "ClipVault - Gerenciador de Clipboard"
            };

            Shell_NotifyIconW(NIM_ADD, ref _nid);

            // Subclass do HWND para capturar mensagens da bandeja
            _subclassProcDelegate = new SubclassProc(WindowSubclassHandler);
            _isSubclassed = SetWindowSubclass(_hwnd, _subclassProcDelegate, (UIntPtr)1, UIntPtr.Zero);
        }
#endif
    }

#if WINDOWS
    private IntPtr WindowSubclassHandler(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WM_TRAYICON)
        {
            var msg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (msg == WM_LBUTTONDBLCLK || msg == WM_LBUTTONUP)
            {
                ShowWindow();
                return IntPtr.Zero;
            }
            else if (msg == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            AppendMenuW(hMenu, MF_STRING, CMD_OPEN, "Abrir ClipVault");
            AppendMenuW(hMenu, MF_STRING, CMD_PAUSE, _isPaused ? "Retomar Monitoramento" : "Pausar Monitoramento");
            AppendMenuW(hMenu, MF_STRING, CMD_CLEAR, "Limpar Histórico");
            AppendMenuW(hMenu, MF_STRING, CMD_SETTINGS, "Configurações");
            AppendMenuW(hMenu, MF_SEPARATOR, 0, string.Empty);
            AppendMenuW(hMenu, MF_STRING, CMD_EXIT, "Sair");

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd);

            int selectedCmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);

            switch (selectedCmd)
            {
                case CMD_OPEN:
                    ShowWindow();
                    RequestShowWindow?.Invoke();
                    break;
                case CMD_PAUSE:
                    _isPaused = !_isPaused;
                    UpdateStatus(_isPaused);
                    RequestToggleMonitoring?.Invoke();
                    break;
                case CMD_CLEAR:
                    RequestClearHistory?.Invoke();
                    break;
                case CMD_SETTINGS:
                    ShowWindow();
                    RequestSettingsWindow?.Invoke();
                    break;
                case CMD_EXIT:
                    Shutdown();
                    RequestExit?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }
#endif

    public void ShowWindow()
    {
#if WINDOWS
        if (_appWindow != null)
        {
            _appWindow.Show();
            SetForegroundWindow(_hwnd);
        }
#endif
    }

    public void HideWindow()
    {
#if WINDOWS
        _appWindow?.Hide();
#endif
    }

    public void UpdateStatus(bool isPaused)
    {
        _isPaused = isPaused;
#if WINDOWS
        _nid.szTip = isPaused ? "ClipVault (Monitoramento Pausado)" : "ClipVault - Ativo";
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
#endif
    }

    public void Shutdown()
    {
#if WINDOWS
        if (_nid.hWnd != IntPtr.Zero)
        {
            Shell_NotifyIconW(NIM_DELETE, ref _nid);
        }

        if (_isSubclassed && _subclassProcDelegate != null)
        {
            RemoveWindowSubclass(_hwnd, _subclassProcDelegate, (UIntPtr)1);
            _isSubclassed = false;
        }

        Environment.Exit(0);
#endif
    }
}
