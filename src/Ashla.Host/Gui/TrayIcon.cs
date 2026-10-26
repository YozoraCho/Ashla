using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ashla.Host.Gui;

internal enum TrayCommand
{
    None = 0,
    OpenLogFile = 1,
    OpenLogFolder = 2,
    OpenPluginsFolder = 3,
    OpenSettingsFolder = 4,
    Quit = 5,

    ToggleVisibility = 6
}

[SupportedOSPlatform("windows")]
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint WM_NULL = 0x0000, WM_CONTEXTMENU = 0x007B, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    private const uint WM_TRAY = 0x8000 + 1;
    private const uint NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4, NIIF_INFO = 0x1, NIIF_WARNING = 0x2;
    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    private const int GWLP_USERDATA = -21;
    private const uint WM_HOTKEY = 0x0312, MOD_NOREPEAT = 0x4000;
    private const int ToggleHotkeyId = 1;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    private static readonly nint HWND_MESSAGE = -3;

    private readonly string _className = $"AshlaTray_{Environment.ProcessId}";
    private readonly nint _classNamePtr;
    private readonly nint _hwnd;
    private readonly nint _icon;
    private readonly bool _ownsIcon;
    private readonly uint _taskbarCreatedMessage;
    private GCHandle _self;
    private string _tooltip;
    private string _status;
    private string? _hotkeyText;
    private bool _hotkeyRegistered;
    private bool _disposed;

    public TrayIcon(string tooltip, string? iconPath)
    {
        _tooltip = tooltip;
        _status = tooltip;
        var instance = GetModuleHandleW(null);
        _classNamePtr = Marshal.StringToHGlobalUni(_className);
        var windowClass = new WndClassEx
        {
            Size = (uint)sizeof(WndClassEx),
            WndProc = &WndProc,
            Instance = instance,
            ClassName = _classNamePtr
        };
        if (RegisterClassExW(ref windowClass) == 0)
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastPInvokeError()}).");

        _hwnd = CreateWindowExW(0, _classNamePtr, _classNamePtr, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, instance, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()}).");
        _self = GCHandle.Alloc(this);
        SetWindowLongPtrW(_hwnd, GWLP_USERDATA, GCHandle.ToIntPtr(_self));
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        if (iconPath is not null && File.Exists(iconPath))
        {
            fixed (char* path = iconPath)
                _icon = LoadImageW(0, path, 1, 0, 0, 0x10 | 0x40);
            _ownsIcon = _icon != 0;
        }
        if (_icon == 0) _icon = LoadIconW(0, (char*)32512);
        Add();
    }

    public event Action<TrayCommand>? Command;

    public void SetStatus(string status)
    {
        if (_disposed || status == _status) return;
        _status = status;
        _tooltip = $"Ashla: {status}";
        var data = NewData(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public string? SetToggleHotkey(uint modifiers, uint virtualKey, string text)
    {
        ClearToggleHotkey();
        if (RegisterHotKey(_hwnd, ToggleHotkeyId, modifiers | MOD_NOREPEAT, virtualKey) == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{text} is already used by another program."
                : $"{text} couldn't be registered (error {error}).";
        }
        _hotkeyRegistered = true;
        _hotkeyText = text;
        return null;
    }

    public void ClearToggleHotkey()
    {
        if (_hotkeyRegistered) UnregisterHotKey(_hwnd, ToggleHotkeyId);
        _hotkeyRegistered = false;
        _hotkeyText = null;
    }

    public static bool IsHotkeyAvailable(uint modifiers, uint virtualKey)
    {
        const int probeId = 0xBEEF;
        if (RegisterHotKey(0, probeId, modifiers | MOD_NOREPEAT, virtualKey) == 0)
            return Marshal.GetLastPInvokeError() != ERROR_HOTKEY_ALREADY_REGISTERED;
        UnregisterHotKey(0, probeId);
        return true;
    }

    public void ShowNotification(string title, string text, bool warning = false)
    {
        if (_disposed) return;
        var data = NewData(NIF_INFO);
        Copy(title, data.InfoTitle, 64);
        Copy(text, data.Info, 256);
        data.InfoFlags = warning ? NIIF_WARNING : NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (Shell_NotifyIconW(NIM_ADD, ref data) == 0)
            throw new InvalidOperationException("Shell_NotifyIcon(NIM_ADD) failed.");
        data.TimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    private NotifyIconData NewData(uint flags)
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = _hwnd,
            Id = 1,
            Flags = flags,
            CallbackMessage = WM_TRAY,
            Icon = _icon
        };
        Copy(_tooltip, data.Tip, 128);
        return data;
    }

    private static void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        for (int i = 0; i < length; i++) destination[i] = text[i];
        destination[length] = '\0';
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MF_STRING | MF_GRAYED, 0, _status);
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, (nuint)TrayCommand.ToggleVisibility,
                _hotkeyText is null ? "Show/Hide Ashla" : $"Show/Hide Ashla\t{_hotkeyText}");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, (nuint)TrayCommand.OpenLogFile, "Open log file");
            AppendMenuW(menu, MF_STRING, (nuint)TrayCommand.OpenLogFolder, "Open log folder");
            AppendMenuW(menu, MF_STRING, (nuint)TrayCommand.OpenPluginsFolder, "Open plugins folder");
            AppendMenuW(menu, MF_STRING, (nuint)TrayCommand.OpenSettingsFolder, "Open settings folder");
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, (nuint)TrayCommand.Quit, "Quit Ashla");

            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            var chosen = (TrayCommand)TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY,
                point.X, point.Y, 0, _hwnd, 0);
            PostMessageW(_hwnd, WM_NULL, 0, 0);
            if (chosen != TrayCommand.None) Command?.Invoke(chosen);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            var state = GetWindowLongPtrW(hwnd, GWLP_USERDATA);
            if (state != 0 && GCHandle.FromIntPtr(state).Target is TrayIcon tray)
            {
                if (message == WM_TRAY)
                {
                    uint trayEvent = (uint)(lParam & 0xFFFF);
                    if (trayEvent is WM_CONTEXTMENU or WM_RBUTTONUP or WM_LBUTTONUP or NIN_SELECT or NIN_KEYSELECT)
                        tray.ShowMenu();
                    return 0;
                }
                if (message == WM_HOTKEY && wParam == ToggleHotkeyId)
                {
                    tray.Command?.Invoke(TrayCommand.ToggleVisibility);
                    return 0;
                }
                if (message == tray._taskbarCreatedMessage && message != 0)
                {
                    tray.Add();
                    return 0;
                }
            }
        }
        catch
        {
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearToggleHotkey();
        var data = NewData(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        SetWindowLongPtrW(_hwnd, GWLP_USERDATA, 0);
        DestroyWindow(_hwnd);
        UnregisterClassW(_classNamePtr, GetModuleHandleW(null));
        Marshal.FreeHGlobal(_classNamePtr);
        if (_ownsIcon) DestroyIcon(_icon);
        if (_self.IsAllocated) _self.Free();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WndClassEx windowClass);

    [LibraryImport("user32.dll")]
    private static partial int UnregisterClassW(nint className, nint instance);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, nint className, nint windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    private static partial int DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static partial nint LoadImageW(nint instance, char* name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint LoadIconW(nint instance, char* name);

    [LibraryImport("user32.dll")]
    private static partial int DestroyIcon(nint icon);

    [LibraryImport("shell32.dll")]
    private static partial int Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    private static partial int DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

    [LibraryImport("user32.dll")]
    private static partial int GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    private static partial int SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    private static partial int UnregisterHotKey(nint hwnd, int id);
}

[SupportedOSPlatform("windows")]
internal static partial class NativeDialogs
{
    public static void ShowError(string title, string message) => MessageBoxW(0, message, title, 0x10);

    public static void ShowInfo(string title, string message) => MessageBoxW(0, message, title, 0x40);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);
}
