using Hexa.NET.ImGui;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Ashla.Rendering;

public sealed record RenderWindowOptions
{
    public string Title { get; init; } = "Ashla";

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 760;
    public bool VSync { get; init; } = true;

    public float? UiScaleOverride { get; init; }

    public string? IniFilePath { get; init; }

    public string? FontPath { get; init; }

    public string? LargeFontPath { get; init; }

    public float LargeFontScale { get; init; } = 1.75f;

    public Action? ConfigureStyle { get; init; }

    public bool Overlay { get; init; }

    public bool Resizable { get; init; } = true;

    public bool CenterOnScreen { get; init; }

    public IReadOnlyList<RenderImage> Icons { get; init; } = [];
}

public sealed class ImGuiWindowHost : IDisposable
{
    private readonly RenderWindowOptions _options;
    private readonly Action<string> _log;
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _controller;
    private bool _shutdownDone;
    private volatile bool _closeRequested;
    private bool _inFrame;
    private bool _shown;
    private bool _passthrough;
    private bool _passthroughSupported = true;
    private bool? _noActivate;
    private (int X, int Y, int Width, int Height) _bounds;

    public ImGuiWindowHost(RenderWindowOptions options, Action<string>? log = null)
    {
        _options = options;
        _log = log ?? (_ => { });
    }

    public event Action? Frame;

    public event Action? BeforeFrame;

    public event Action? Shutdown;

    public event Action<Exception>? FrameError;

    public float UiScale => _controller?.UiScale ?? 1f;

    public void RefreshStyle() => _controller?.RebuildStyle();

    public ImFontPtr LargeFont => _controller?.LargeFont ?? default;

    public float LargeFontSize => _controller?.LargeFontSize ?? 0f;

    public string? GraphicsInfo { get; private set; }

    public bool IsOverlay => _options.Overlay;

    public nint NativeHandle { get; private set; }

    public bool IsShown => !_options.Overlay || _shown;

    public bool IsMousePassthrough => _passthrough;

    public void SetBounds(int x, int y, int width, int height)
    {
        if (_window is null || width <= 0 || height <= 0 || _bounds == (x, y, width, height)) return;
        _bounds = (x, y, width, height);
        _window.Position = new Vector2D<int>(x, y);
        _window.Size = new Vector2D<int>(width, height);
    }

    public void SetShown(bool shown)
    {
        if (!_options.Overlay || _window is null || shown == _shown) return;
        _shown = shown;
        if (!shown) _controller?.ResetInput();
        _window.IsVisible = shown;
        if (shown) BringToFront();
    }

    public void BringToFront()
    {
        if (_options.Overlay && NativeHandle != 0 && OperatingSystem.IsWindows()) Win32Overlay.BringToTop(NativeHandle);
    }

    public void RequestClose() => _closeRequested = true;

    public void Run()
    {
        NativeLibraryPaths.Register();
        var windowOptions = WindowOptions.Default with
        {
            Title = _options.Title,
            Size = new Vector2D<int>(_options.Width, _options.Height),
            VSync = _options.VSync,
            FramesPerSecond = 240,
            UpdatesPerSecond = 240,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
            ShouldSwapAutomatically = true,
            TransparentFramebuffer = _options.Overlay,
            WindowBorder = _options.Overlay ? WindowBorder.Hidden
                : _options.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed,
            TopMost = _options.Overlay,
            IsVisible = !_options.Overlay
        };

        _window = Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += OnClosing;
        try
        {
            _window.Run();
        }
        finally
        {
            RunShutdown();
            _window.Dispose();
            _window = null;
        }
    }

    private void OnLoad()
    {
        var window = _window!;
        _gl = GL.GetApi(window);
        GraphicsInfo = $"{_gl.GetStringS(StringName.Renderer)} | OpenGL {_gl.GetStringS(StringName.Version)}";
        _log($"Graphics: {GraphicsInfo}");
        _input = window.CreateInput();
        _controller = new ImGuiController(window, _gl, _input, _options, _log);
        NativeHandle = window.Native?.Win32?.Hwnd ?? 0;
        SetIcons(window);
        if (_options.Overlay) ConfigureOverlay(window);
        else
        {
            ScaleInitialWindowForDpi(window);
            if (_options.CenterOnScreen) CenterOnMonitor(window);
        }
    }

    private void SetIcons(IWindow window)
    {
        if (_options.Icons.Count == 0) return;
        try
        {
            var icons = _options.Icons.Select(i => new Silk.NET.Core.RawImage(i.Width, i.Height, i.Rgba)).ToArray();
            window.SetWindowIcon(icons);
        }
        catch (Exception error)
        {
            _log($"Could not set the window icon: {error.Message}");
        }
    }

    public ImTextureRef? CreateTexture(RenderImage image)
    {
        if (_controller is null) return null;
        return _controller.CreateTexture(image);
    }

    private static void CenterOnMonitor(IWindow window)
    {
        if (window.Monitor is not { } monitor) return;
        var bounds = monitor.Bounds;
        var size = window.Size;
        window.Position = new Vector2D<int>(bounds.Origin.X + Math.Max(0, (bounds.Size.X - size.X) / 2),
            bounds.Origin.Y + Math.Max(0, (bounds.Size.Y - size.Y) / 2));
    }

    private void ConfigureOverlay(IWindow window)
    {
        GlfwOverlay.DisableFocusOnShow(window);
        if (NativeHandle != 0 && OperatingSystem.IsWindows()) Win32Overlay.ApplyStyles(NativeHandle);
        SetPassthrough(true);
        SetNoActivate(true);
        _log(_passthroughSupported
            ? "Overlay window ready (transparent, click-through outside Ashla windows)."
            : "Overlay window ready, but click-through is unavailable: the overlay will block mouse input to the game.");
    }

    private void SetNoActivate(bool noActivate)
    {
        if (NativeHandle == 0 || !OperatingSystem.IsWindows() || noActivate == _noActivate) return;
        Win32Overlay.SetNoActivate(NativeHandle, noActivate);
        _noActivate = noActivate;
    }

    private void SetPassthrough(bool enabled)
    {
        if (!_passthroughSupported || _window is null || enabled == _passthrough) return;
        try
        {
            if (!GlfwOverlay.SetMousePassthrough(_window, enabled)) _passthroughSupported = false;
            else _passthrough = enabled;
        }
        catch (Exception error)
        {
            _passthroughSupported = false;
            _log($"Mouse passthrough not supported: {error.Message}");
        }
    }

    private void ScaleInitialWindowForDpi(IWindow window)
    {
        float framebufferScale = window.Size.X > 0 ? (float)window.FramebufferSize.X / window.Size.X : 1f;
        float scale = _controller!.UiScale;
        if (scale <= 1.01f || framebufferScale > 1.01f) return;

        var size = new Vector2D<int>((int)(_options.Width * scale), (int)(_options.Height * scale));
        if (window.Monitor is { } monitor)
        {
            var bounds = monitor.Bounds;
            size = new Vector2D<int>(Math.Min(size.X, bounds.Size.X), Math.Min(size.Y, bounds.Size.Y - 80));
            window.Size = size;
            window.Position = new Vector2D<int>(bounds.Origin.X + (bounds.Size.X - size.X) / 2,
                bounds.Origin.Y + (bounds.Size.Y - size.Y) / 2);
        }
        else
        {
            window.Size = size;
        }
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        if (size.X > 0 && size.Y > 0) _gl?.Viewport(0, 0, (uint)size.X, (uint)size.Y);
    }

    private void OnRender(double deltaSeconds)
    {
        if (_gl is null || _controller is null || _window is null) return;

        try
        {
            BeforeFrame?.Invoke();
        }
        catch (Exception error)
        {
            FrameError?.Invoke(error);
        }

        if (_closeRequested)
        {
            _window.Close();
            return;
        }

        if (_options.Overlay && !_shown)
        {
            Thread.Sleep(50);
            return;
        }

        var framebuffer = _window.FramebufferSize;
        if (framebuffer.X <= 0 || framebuffer.Y <= 0)
        {
            Thread.Sleep(16);
            return;
        }

        _inFrame = true;
        try
        {
            _controller.NewFrame((float)deltaSeconds, pollCursor: _options.Overlay);
            try
            {
                Frame?.Invoke();
            }
            catch (Exception error)
            {
                FrameError?.Invoke(error);
            }

            _gl.Viewport(0, 0, (uint)framebuffer.X, (uint)framebuffer.Y);
            if (_options.Overlay) _gl.ClearColor(0f, 0f, 0f, 0f);
            else _gl.ClearColor(0.07f, 0.08f, 0.10f, 1f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
            _controller.Render();

            if (_options.Overlay)
            {
                var io = ImGui.GetIO();
                SetPassthrough(!io.WantCaptureMouse);
                SetNoActivate(!io.WantTextInput);
            }
        }
        finally
        {
            _inFrame = false;
        }

        if (_closeRequested) _window.Close();
    }

    private void OnClosing()
    {
        if (_inFrame)
        {
            _closeRequested = true;
            return;
        }
        RunShutdown();
    }

    private void RunShutdown()
    {
        if (_shutdownDone) return;
        _shutdownDone = true;
        try
        {
            Shutdown?.Invoke();
        }
        finally
        {
            _controller?.Dispose();
            _controller = null;
            _input?.Dispose();
            _input = null;
            _gl?.Dispose();
            _gl = null;
        }
    }

    public void Dispose()
    {
        RunShutdown();
        _window?.Dispose();
        _window = null;
    }

    public static string ImGuiVersion => ImGui.GetVersionS();
}
