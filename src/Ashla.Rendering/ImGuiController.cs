using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Hexa.NET.ImGui;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

namespace Ashla.Rendering;

internal sealed unsafe class ImGuiController : IDisposable
{
    private const float DefaultFontSize = 13f;
    private const float TrueTypeFontSize = 16f;

    private readonly IWindow _window;
    private readonly IInputContext _input;
    private readonly ImGuiGlRenderer _renderer;
    private readonly RenderWindowOptions _options;
    private readonly Dictionary<string, ImFontPtr> _extraFonts = new(StringComparer.Ordinal);
    private readonly Action<string> _log;
    private readonly ImGuiContextPtr _context;
    private ImGuiStyle _baseStyle;
    private readonly HashSet<Key> _modifiersDown = [];
    private GCHandle _selfHandle;
    private byte* _iniPath;
    private byte* _clipboardBuffer;
    private IKeyboard? _clipboardKeyboard;
    private ImGuiMouseCursor _lastCursor = (ImGuiMouseCursor)(-2);
    private float _appliedContentScale;
    private delegate* unmanaged[Cdecl]<void*, float*, float*, void> _getWindowContentScale;
    private bool _disposed;

    public ImGuiController(IWindow window, GL gl, IInputContext input, RenderWindowOptions options, Action<string> log)
    {
        _window = window;
        _input = input;
        _options = options;
        _log = log;

        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors | ImGuiBackendFlags.RendererHasVtxOffset
            | ImGuiBackendFlags.RendererHasTextures;
        io.ConfigErrorRecovery = true;
        io.ConfigErrorRecoveryEnableAssert = false;
        io.ConfigErrorRecoveryEnableDebugLog = true;
        io.ConfigErrorRecoveryEnableTooltip = true;
        SetIniPath(io, options.IniFilePath);

        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        style.WindowRounding = 6f;
        style.FrameRounding = 4f;
        style.GrabRounding = 4f;
        options.ConfigureStyle?.Invoke();

        _renderer = new ImGuiGlRenderer(gl);
        LoadFonts(io);
        style.FontSizeBase = BaseFontSize;
        _baseStyle = *style.Handle;
        InstallClipboard();
        AttachInput();
        _window.FocusChanged += OnFocusChanged;
        UpdateScale(force: true);
    }

    public void RebuildStyle()
    {
        var style = ImGui.GetStyle();
        *style.Handle = _baseStyle;
        _options.ConfigureStyle?.Invoke();
        style.FontSizeBase = BaseFontSize;
        _baseStyle = *style.Handle;
        style.ScaleAllSizes(UiScale);
        style.FontScaleDpi = UiScale;
    }

    public float ContentScale => _appliedContentScale;

    public float UiScale { get; private set; } = 1f;

    public ImFontPtr LargeFont { get; private set; }

    public float LargeFontSize => BaseFontSize * _options.LargeFontScale;

    public float BaseFontSize { get; private set; } = DefaultFontSize;

    public void NewFrame(float deltaSeconds, bool pollCursor = false)
    {
        ImGui.SetCurrentContext(_context);
        UpdateScale(force: false);
        var io = ImGui.GetIO();
        if (pollCursor && GlfwOverlay.TryGetCursorPosition(_window, out var cursor))
            io.AddMousePosEvent(cursor.X, cursor.Y);
        io.DeltaTime = deltaSeconds > 0 ? deltaSeconds : 1f / 60f;
        ImGui.NewFrame();
    }

    public void ResetInput()
    {
        ImGui.SetCurrentContext(_context);
        _modifiersDown.Clear();
        ImGui.GetIO().AddFocusEvent(false);
    }

    public void Render()
    {
        ImGui.Render();
        UpdateCursor();
        _renderer.Render(ImGui.GetDrawData());
    }

    public unsafe ImTextureRef CreateTexture(RenderImage image) =>
        new(null, new ImTextureID((ulong)_renderer.CreateStaticTexture(image)));

    public ImGuiContextPtr Context => _context;

    public float QueryContentScale()
    {
        if (_options.UiScaleOverride is > 0 and var scale) return scale;
        try
        {
            if (GlfwWindowing.IsViewGlfw(_window))
            {
                if (_getWindowContentScale == null)
                {
                    var glfw = GlfwWindowing.GetExistingApi(_window);
                    if (glfw is not null && glfw.Context.TryGetProcAddress("glfwGetWindowContentScale", out var address, null))
                        _getWindowContentScale = (delegate* unmanaged[Cdecl]<void*, float*, float*, void>)address;
                }
                if (_getWindowContentScale != null)
                {
                    float x = 1f, y = 1f;
                    _getWindowContentScale(GlfwWindowing.GetHandle(_window), &x, &y);
                    float s = MathF.Max(x, y);
                    if (s > 0.25f && s < 8f) return s;
                }
            }
        }
        catch (Exception error)
        {
            _log($"Could not query monitor content scale ({error.Message}); using 1.0.");
        }
        return 1f;
    }

    private void UpdateScale(bool force)
    {
        var io = ImGui.GetIO();
        var size = _window.Size;
        var framebuffer = _window.FramebufferSize;
        io.DisplaySize = new Vector2(size.X, size.Y);
        float framebufferScale = 1f;
        if (size.X > 0 && size.Y > 0)
        {
            io.DisplayFramebufferScale = new Vector2((float)framebuffer.X / size.X, (float)framebuffer.Y / size.Y);
            framebufferScale = io.DisplayFramebufferScale.X;
        }
        if (framebufferScale <= 0) framebufferScale = 1f;

        float contentScale = QueryContentScale();
        if (!force && MathF.Abs(contentScale - _appliedContentScale) < 0.01f) return;

        UiScale = MathF.Max(0.5f, contentScale / framebufferScale);
        var style = ImGui.GetStyle();
        *style.Handle = _baseStyle;
        style.ScaleAllSizes(UiScale);
        style.FontScaleDpi = UiScale;
        _appliedContentScale = contentScale;
        _log($"UI scale {UiScale:0.##} (content scale {contentScale:0.##}, framebuffer scale {framebufferScale:0.##}).");
    }

    public ImFontPtr ExtraFont(string name) => _extraFonts.TryGetValue(name, out var font) ? font : default;

    private ImFontPtr AddFont(ImGuiIOPtr io, byte[] data, float size)
    {
        if (data.Length == 0) return default;
        void* copy = ImGui.MemAlloc((nuint)data.Length);
        fixed (byte* source = data) System.Buffer.MemoryCopy(source, copy, data.Length, data.Length);
        return io.Fonts.AddFontFromMemoryTTF(copy, data.Length, size);
    }

    private void LoadFonts(ImGuiIOPtr io)
    {
        var fontPath = FindFont();
        ImFontPtr regular = _options.FontData is { } fontData ? AddFont(io, fontData, TrueTypeFontSize)
            : fontPath is not null ? io.Fonts.AddFontFromFileTTF(fontPath, TrueTypeFontSize) : default;
        if (regular.IsNull)
        {
            if (fontPath is not null) _log($"Could not load font {fontPath}; using Dear ImGui's built-in font.");
            regular = io.Fonts.AddFontDefault();
            BaseFontSize = DefaultFontSize;
            LargeFont = regular;
            return;
        }
        BaseFontSize = TrueTypeFontSize;

        var largePath = FindLargeFont();
        ImFontPtr large = _options.LargeFontData is { } largeData ? AddFont(io, largeData, TrueTypeFontSize * _options.LargeFontScale)
            : largePath is not null && largePath != fontPath
                ? io.Fonts.AddFontFromFileTTF(largePath, TrueTypeFontSize * _options.LargeFontScale)
                : default;
        LargeFont = large.IsNull ? regular : large;

        foreach (var (name, data) in _options.ExtraFonts)
        {
            var extra = AddFont(io, data, TrueTypeFontSize);
            if (!extra.IsNull) _extraFonts[name] = extra;
            else _log($"Could not load font {name}.");
        }
    }

    private string? FindLargeFont()
    {
        IEnumerable<string?> candidates =
        [
            _options.LargeFontPath,
            OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguisb.ttf")
                : null,
            OperatingSystem.IsLinux() ? "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" : null
        ];
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }

    private string? FindFont()
    {
        IEnumerable<string?> candidates =
        [
            _options.FontPath,
            OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf")
                : null,
            OperatingSystem.IsLinux() ? "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf" : null
        ];
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }

    private void SetIniPath(ImGuiIOPtr io, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            io.IniFilename = null;
            return;
        }
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); }
        catch (Exception error) { _log($"Could not create {path} directory: {error.Message}"); }
        _iniPath = AllocUtf8(path);
        io.IniFilename = _iniPath;
    }

    private void AttachInput()
    {
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnScroll;
        }
    }

    private void DetachInput()
    {
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
            keyboard.KeyChar -= OnKeyChar;
        }
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove -= OnMouseMove;
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.Scroll -= OnScroll;
        }
    }

    private void OnFocusChanged(bool focused) => ImGui.GetIO().AddFocusEvent(focused);

    private void OnMouseMove(IMouse mouse, Vector2 position) => ImGui.GetIO().AddMousePosEvent(position.X, position.Y);

    private void OnMouseDown(IMouse mouse, MouseButton button) => AddMouseButton(button, true);

    private void OnMouseUp(IMouse mouse, MouseButton button) => AddMouseButton(button, false);

    private static void AddMouseButton(MouseButton button, bool down)
    {
        int index = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            MouseButton.Button4 => 3,
            MouseButton.Button5 => 4,
            _ => -1
        };
        if (index >= 0) ImGui.GetIO().AddMouseButtonEvent(index, down);
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel) => ImGui.GetIO().AddMouseWheelEvent(wheel.X, wheel.Y);

    private void OnKeyChar(IKeyboard keyboard, char character) => ImGui.GetIO().AddInputCharacterUTF16(character);

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) => AddKey(key, scancode, true);

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode) => AddKey(key, scancode, false);

    private void AddKey(Key key, int scancode, bool down)
    {
        var io = ImGui.GetIO();
        if (IsModifier(key))
        {
            if (down) _modifiersDown.Add(key); else _modifiersDown.Remove(key);
            io.AddKeyEvent(ImGuiKey.ModCtrl, _modifiersDown.Contains(Key.ControlLeft) || _modifiersDown.Contains(Key.ControlRight));
            io.AddKeyEvent(ImGuiKey.ModShift, _modifiersDown.Contains(Key.ShiftLeft) || _modifiersDown.Contains(Key.ShiftRight));
            io.AddKeyEvent(ImGuiKey.ModAlt, _modifiersDown.Contains(Key.AltLeft) || _modifiersDown.Contains(Key.AltRight));
            io.AddKeyEvent(ImGuiKey.ModSuper, _modifiersDown.Contains(Key.SuperLeft) || _modifiersDown.Contains(Key.SuperRight));
        }

        var imguiKey = KeyMap.ToImGui(key);
        if (imguiKey == ImGuiKey.None) return;
        io.AddKeyEvent(imguiKey, down);
        io.SetKeyEventNativeData(imguiKey, (int)key, scancode);
    }

    private static bool IsModifier(Key key) => key is Key.ControlLeft or Key.ControlRight or Key.ShiftLeft
        or Key.ShiftRight or Key.AltLeft or Key.AltRight or Key.SuperLeft or Key.SuperRight;

    private void UpdateCursor()
    {
        var io = ImGui.GetIO();
        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0) return;
        var wanted = io.MouseDrawCursor ? ImGuiMouseCursor.None : ImGui.GetMouseCursor();
        if (wanted == _lastCursor) return;
        _lastCursor = wanted;

        foreach (var mouse in _input.Mice)
        {
            var cursor = mouse.Cursor;
            if (wanted == ImGuiMouseCursor.None)
            {
                cursor.CursorMode = CursorMode.Hidden;
                continue;
            }
            cursor.CursorMode = CursorMode.Normal;
            var standard = wanted switch
            {
                ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
                ImGuiMouseCursor.ResizeAll => StandardCursor.ResizeAll,
                ImGuiMouseCursor.ResizeNs => StandardCursor.VResize,
                ImGuiMouseCursor.ResizeEw => StandardCursor.HResize,
                ImGuiMouseCursor.ResizeNesw => StandardCursor.NeswResize,
                ImGuiMouseCursor.ResizeNwse => StandardCursor.NwseResize,
                ImGuiMouseCursor.Hand => StandardCursor.Hand,
                ImGuiMouseCursor.NotAllowed => StandardCursor.NotAllowed,
                _ => StandardCursor.Default
            };
            cursor.StandardCursor = cursor.IsSupported(standard) ? standard : StandardCursor.Default;
        }
    }

    private void InstallClipboard()
    {
        _clipboardKeyboard = _input.Keyboards.FirstOrDefault();
        if (_clipboardKeyboard is null) return;
        _selfHandle = GCHandle.Alloc(this);
        var platform = ImGui.GetPlatformIO();
        platform.PlatformClipboardUserData = (void*)GCHandle.ToIntPtr(_selfHandle);
        platform.PlatformGetClipboardTextFn = (delegate* unmanaged[Cdecl]<nint, byte*>)&GetClipboardText;
        platform.PlatformSetClipboardTextFn = (delegate* unmanaged[Cdecl]<nint, byte*, void>)&SetClipboardText;
    }

    private static ImGuiController? FromClipboardUserData()
    {
        var data = (nint)ImGui.GetPlatformIO().PlatformClipboardUserData;
        return data == nint.Zero ? null : GCHandle.FromIntPtr(data).Target as ImGuiController;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte* GetClipboardText(nint context)
    {
        try
        {
            var self = FromClipboardUserData();
            if (self?._clipboardKeyboard is null) return null;
            if (self._clipboardBuffer != null) NativeMemory.Free(self._clipboardBuffer);
            self._clipboardBuffer = AllocUtf8(self._clipboardKeyboard.ClipboardText ?? string.Empty);
            return self._clipboardBuffer;
        }
        catch
        {
            return null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetClipboardText(nint context, byte* text)
    {
        try
        {
            var self = FromClipboardUserData();
            if (self?._clipboardKeyboard is null || text == null) return;
            self._clipboardKeyboard.ClipboardText = Marshal.PtrToStringUTF8((nint)text) ?? string.Empty;
        }
        catch
        {
        }
    }

    private static byte* AllocUtf8(string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        var buffer = (byte*)NativeMemory.Alloc((nuint)count + 1);
        Encoding.UTF8.GetBytes(value, new Span<byte>(buffer, count));
        buffer[count] = 0;
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.FocusChanged -= OnFocusChanged;
        DetachInput();
        ImGui.SetCurrentContext(_context);
        var platform = ImGui.GetPlatformIO();
        platform.PlatformGetClipboardTextFn = null;
        platform.PlatformSetClipboardTextFn = null;
        platform.PlatformClipboardUserData = null;
        _renderer.DestroyAllTextures();
        _renderer.Dispose();
        ImGui.DestroyContext(_context);
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        if (_iniPath != null) NativeMemory.Free(_iniPath);
        if (_clipboardBuffer != null) NativeMemory.Free(_clipboardBuffer);
        _iniPath = null;
        _clipboardBuffer = null;
    }
}
