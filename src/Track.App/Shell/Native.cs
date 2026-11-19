using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Track.Core.Settings;
using Track.Core.Tray;

namespace Track.App.Shell;

/// <summary>Thin Windows calls. Each method is a few lines; behavior is checked by running the app.</summary>
[ExcludeFromCodeCoverage(Justification = "Windows API wrappers; verified manually and by screenshots.")]
internal static partial class Native
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropMainWindow = 2;
    private const int BackdropTransientWindow = 3;
    private const int BackdropTabbedWindow = 4;
    internal const int HotkeyMessage = 0x0312;
    internal const uint HotkeyModAlt = 0x0001;
    internal const uint HotkeyModControl = 0x0002;
    internal const uint HotkeyModShift = 0x0004;
    internal const uint HotkeyModWindows = 0x0008;
    internal const uint HotkeyModNoRepeat = 0x4000;
    private const int CornerRound = 2;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;

    /// <summary>Applies the selected DWM material, rounded corners and title colors. False when a material is unsupported.</summary>
    public static bool ApplyBackdrop(Window window, bool dark, WindowMaterial material)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(hwnd)!.CompositionTarget!.BackgroundColor = Colors.Transparent;
        var darkFlag = dark ? 1 : 0;
        var corner = CornerRound;
        var backdrop = material switch
        {
            WindowMaterial.Acrylic => BackdropTransientWindow,
            WindowMaterial.Mica => BackdropMainWindow,
            WindowMaterial.MicaAlt => BackdropTabbedWindow,
            WindowMaterial.Solid => BackdropNone,
            _ => BackdropNone,
        };
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref darkFlag, sizeof(int));
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref corner, sizeof(int));
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var result = DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int));
        return material != WindowMaterial.Solid && result == 0;
    }

    /// <summary>Never takes focus and stays out of Alt+Tab.</summary>
    public static void MakeToolWindow(Window window, bool noActivate)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var style = GetWindowLong(hwnd, GwlExStyle) | WsExToolWindow | (noActivate ? WsExNoActivate : 0);
        SetWindowLong(hwnd, GwlExStyle, style);
    }

    /// <summary>Lets a tool window take focus (opened island) or never take it (compact island).</summary>
    public static void SetNoActivate(Window window, bool noActivate)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, noActivate ? style | WsExNoActivate : style & ~WsExNoActivate);
    }

    /// <summary>
    /// Makes the window the foreground window after the user clicked it (used for the opened island, so Escape
    /// and clicking elsewhere work). Windows only lets the foreground thread hand over focus, so for this call
    /// Track joins that thread's input queue, then detaches again.
    /// </summary>
    public static void BringToFront(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var ownThread = GetCurrentThreadId();
        var attached = foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
        var set = SetForegroundWindow(hwnd);
        if (attached)
        {
            AttachThreadInput(ownThread, foregroundThread, false);
        }

        LastBringToFront = $"attached={attached} set={set} now-front={GetForegroundWindow() == hwnd}";
    }

    /// <summary>What the last <see cref="BringToFront"/> did (diagnostics only).</summary>
    public static string LastBringToFront { get; private set; } = "-";

    /// <summary>True while a full-screen app, game, video or presentation is in front (QUNS_BUSY, D3D, presentation).</summary>
    public static bool FullScreenAppInFront() =>
        SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;

    /// <summary>The work area (device-independent pixels) of the monitor under a point in screen pixels.</summary>
    public static Rect MonitorWorkArea(Point pixels)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = (int)pixels.X, Y = (int)pixels.Y }, 2 /* nearest */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return SystemParameters.WorkArea;
        }

        var scale = SystemDpiScale();
        return new Rect(info.Work.Left / scale, info.Work.Top / scale, (info.Work.Right - info.Work.Left) / scale, (info.Work.Bottom - info.Work.Top) / scale);
    }

    /// <summary>Lets Windows take back pages Track is not using right now.</summary>
    public static void TrimWorkingSet() => SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);

    /// <summary>True when the left mouse button is down now or was pressed since the last check.</summary>
    public static bool LeftButtonPressed() => (GetAsyncKeyState(0x01) & 0x8001) != 0;

    public static Point CursorPixels() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(0, 0);

    /// <summary>
    /// Current scaling of the main monitor (1.0 = 100 %, 1.5 = 150 %). Read live from the monitor rather than
    /// GetDpiForSystem, which keeps the value from sign-in, so a scaling change resizes the tray icon at once.
    /// </summary>
    public static double SystemDpiScale()
    {
        var monitor = MonitorFromPoint(new NativePoint { X = 0, Y = 0 }, 1 /* primary */);
        return GetDpiForMonitor(monitor, 0 /* effective */, out var dpiX, out _) == 0 ? dpiX / 96.0 : GetDpiForSystem() / 96.0;
    }

    public static void DestroyIconHandle(IntPtr handle) => DestroyIcon(handle);

    public static bool RegisterHotkey(IntPtr hwnd, int id, uint modifiers, uint virtualKey) =>
        RegisterHotKeyNative(hwnd, id, modifiers, virtualKey);

    public static bool UnregisterHotkey(IntPtr hwnd, int id) => UnregisterHotKeyNative(hwnd, id);

    /// <summary>Light or dark for app windows (AppsUseLightTheme) or the taskbar (SystemUsesLightTheme).</summary>
    public static TaskbarTheme ReadTheme(bool taskbar)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(taskbar ? "SystemUsesLightTheme" : "AppsUseLightTheme") is int light && light == 0
            ? TaskbarTheme.Dark
            : TaskbarTheme.Light;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);

    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKeyNative(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKeyNative(IntPtr hwnd, int id);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(IntPtr process, nint minimum, nint maximum);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint point, int flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}

/// <summary>The HKCU Run entry that starts Track with Windows.</summary>
[ExcludeFromCodeCoverage(Justification = "Registry wrapper; the decision logic (StartupManager) is tested in Track.Core.")]
internal sealed class StartupRegistry : IStartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Track";

    public string? Get()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) as string;
    }

    public void Set(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(Name, command);
    }

    public void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(Name, throwOnMissingValue: false);
    }
}
