using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.GLFW;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

namespace Ashla.Rendering;

internal static unsafe class GlfwOverlay
{
    private const int MousePassthroughAttribute = 0x0002000D;

    public static bool SetMousePassthrough(IWindow window, bool enabled)
    {
        if (!GlfwWindowing.IsViewGlfw(window)) return false;
        var glfw = GlfwWindowing.GetExistingApi(window);
        if (glfw is null) return false;
        glfw.SetWindowAttrib(GlfwWindowing.GetHandle(window), (WindowAttributeSetter)MousePassthroughAttribute, enabled);
        return true;
    }

    public static void DisableFocusOnShow(IWindow window)
    {
        if (!GlfwWindowing.IsViewGlfw(window)) return;
        GlfwWindowing.GetExistingApi(window)?.SetWindowAttrib(GlfwWindowing.GetHandle(window),
            WindowAttributeSetter.FocusOnShow, false);
    }

    public static bool TryGetCursorPosition(IWindow window, out Vector2 position)
    {
        position = default;
        if (!GlfwWindowing.IsViewGlfw(window)) return false;
        var glfw = GlfwWindowing.GetExistingApi(window);
        if (glfw is null) return false;
        glfw.GetCursorPos(GlfwWindowing.GetHandle(window), out double x, out double y);
        position = new Vector2((float)x, (float)y);
        return true;
    }
}

[SupportedOSPlatform("windows")]
internal static partial class Win32Overlay
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const long WS_EX_LAYERED = 0x00080000;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x00000002;
    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

    public static void ApplyStyles(nint hwnd)
    {
        long exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        exStyle = (exStyle | WS_EX_TOOLWINDOW | WS_EX_LAYERED) & ~WS_EX_APPWINDOW;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (nint)exStyle);
        if (SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA) == 0)
            throw new InvalidOperationException($"SetLayeredWindowAttributes failed ({Marshal.GetLastPInvokeError()}).");
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    public static void SetNoActivate(nint hwnd, bool noActivate)
    {
        long exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        exStyle = noActivate ? exStyle | WS_EX_NOACTIVATE : exStyle & ~WS_EX_NOACTIVATE;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (nint)exStyle);
        if (!noActivate) SetForegroundWindow(hwnd);
    }

    public static void BringToTop(nint hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int SetForegroundWindow(nint hwnd);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
}
