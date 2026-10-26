using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ashla.Host.Overlay;

[SupportedOSPlatform("windows")]
internal sealed unsafe partial class Win32WindowSystem : IWindowSystem
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;

    public IReadOnlyList<int> FindProcessIds(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return [.. processes.Select(p => p.Id)];
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    public IReadOnlyList<TopLevelWindow> GetTopLevelWindows(int processId)
    {
        var search = new WindowSearch(processId);
        var handle = GCHandle.Alloc(search);
        try
        {
            EnumWindows(&EnumWindowsCallback, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return search.Results;
    }

    [UnmanagedCallersOnly]
    private static int EnumWindowsCallback(nint window, nint state)
    {
        try
        {
            var search = (WindowSearch)GCHandle.FromIntPtr(state).Target!;
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != search.ProcessId || IsWindowVisible(window) == 0) return 1;
            long exStyle = IntPtr.Size == 8 ? GetWindowLongPtrW(window, GWL_EXSTYLE) : GetWindowLongW(window, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return 1;
            int length = GetWindowTextLengthW(window);
            if (length <= 0) return 1;

            var buffer = new char[length + 1];
            int copied;
            fixed (char* text = buffer) copied = GetWindowTextW(window, text, buffer.Length);
            search.Results.Add(new TopLevelWindow(window, new string(buffer, 0, Math.Max(0, copied))));
        }
        catch
        {
        }
        return 1;
    }

    public bool IsWindow(nint handle) => IsWindowNative(handle) != 0;

    public bool IsMinimized(nint handle) => IsIconic(handle) != 0;

    public bool TryGetClientBounds(nint handle, out WindowBounds bounds)
    {
        bounds = default;
        if (GetClientRect(handle, out var rect) == 0) return false;
        var origin = new Point { X = rect.Left, Y = rect.Top };
        if (ClientToScreen(handle, ref origin) == 0) return false;
        bounds = new WindowBounds(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return true;
    }

    public nint GetForegroundWindow() => GetForegroundWindowNative();

    private sealed class WindowSearch(int processId)
    {
        public uint ProcessId { get; } = (uint)processId;
        public List<TopLevelWindow> Results { get; } = [];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll")]
    private static partial int EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(nint window);

    [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
    private static partial int IsWindowNative(nint window);

    [LibraryImport("user32.dll")]
    private static partial int IsIconic(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint window, int index);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint window, int index);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(nint window);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextW(nint window, char* text, int maxCount);

    [LibraryImport("user32.dll")]
    private static partial int GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial int ClientToScreen(nint window, ref Point point);

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindowNative();
}
