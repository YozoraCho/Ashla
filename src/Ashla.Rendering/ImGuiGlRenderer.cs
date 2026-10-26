using System.Numerics;
using System.Runtime.CompilerServices;
using Hexa.NET.ImGui;
using Silk.NET.OpenGL;

namespace Ashla.Rendering;

internal sealed unsafe class ImGuiGlRenderer : IDisposable
{
    private const string VertexSource = """
        #version 330 core
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy, 0.0, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 330 core
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }
        """;

    private readonly GL _gl;
    private readonly uint _program;
    private readonly int _projectionLocation;
    private readonly int _textureLocation;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly List<uint> _staticTextures = [];
    private bool _disposed;

    public ImGuiGlRenderer(GL gl)
    {
        _gl = gl;
        _program = CreateProgram();
        _projectionLocation = _gl.GetUniformLocation(_program, "ProjMtx");
        _textureLocation = _gl.GetUniformLocation(_program, "Texture");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        uint stride = (uint)Unsafe.SizeOf<ImDrawVert>();
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)16);
        _gl.BindVertexArray(0);
    }

    public uint CreateStaticTexture(RenderImage image)
    {
        if (image.Rgba.Length < image.Width * image.Height * 4)
            throw new ArgumentException("Image data is smaller than width × height × 4.", nameof(image));
        uint id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, id);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        fixed (byte* pixels = image.Rgba)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)image.Width, (uint)image.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _staticTextures.Add(id);
        return id;
    }

    public static bool SupportsFormat(ImTextureFormat format) => format == ImTextureFormat.Rgba32;

    public void UpdateTexture(ImTextureDataPtr texture)
    {
        switch (texture.Status)
        {
            case ImTextureStatus.WantCreate:
                CreateTexture(texture);
                break;
            case ImTextureStatus.WantUpdates:
                UploadUpdates(texture);
                break;
            case ImTextureStatus.WantDestroy when texture.UnusedFrames > 0:
                DestroyTexture(texture);
                break;
        }
    }

    private void CreateTexture(ImTextureDataPtr texture)
    {
        if (!SupportsFormat(texture.Format))
            throw new NotSupportedException($"ImGui texture format {texture.Format} is not supported by this renderer.");

        uint id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, id);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)texture.Width, (uint)texture.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, texture.GetPixels());
        _gl.BindTexture(TextureTarget.Texture2D, 0);

        texture.SetTexID(new ImTextureID((ulong)id));
        texture.SetStatus(ImTextureStatus.Ok);
    }

    private void UploadUpdates(ImTextureDataPtr texture)
    {
        _gl.BindTexture(TextureTarget.Texture2D, (uint)texture.TexID.Handle);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, texture.Width);
        ref var updates = ref texture.Updates;
        for (int i = 0; i < updates.Size; i++)
        {
            var rect = updates.Data[i];
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, rect.X, rect.Y, rect.W, rect.H,
                PixelFormat.Rgba, PixelType.UnsignedByte, texture.GetPixelsAt(rect.X, rect.Y));
        }
        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        texture.SetStatus(ImTextureStatus.Ok);
    }

    private void DestroyTexture(ImTextureDataPtr texture)
    {
        uint id = (uint)texture.TexID.Handle;
        if (id != 0) _gl.DeleteTexture(id);
        texture.SetTexID(ImTextureID.Null);
        texture.SetStatus(ImTextureStatus.Destroyed);
    }

    public void DestroyAllTextures()
    {
        ref var textures = ref ImGui.GetPlatformIO().Textures;
        for (int i = 0; i < textures.Size; i++)
        {
            var texture = textures.Data[i];
            if (texture.RefCount == 1 && texture.TexID.Handle != 0) DestroyTexture(texture);
        }
    }

    public void Render(ImDrawDataPtr drawData)
    {
        var textures = drawData.Handle->Textures;
        if (textures != null)
        {
            for (int i = 0; i < textures->Size; i++)
            {
                var texture = textures->Data[i];
                if (texture.Status != ImTextureStatus.Ok) UpdateTexture(texture);
            }
        }

        int framebufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int framebufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0 || drawData.CmdLists.Size == 0) return;

        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);

        float left = drawData.DisplayPos.X;
        float right = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float top = drawData.DisplayPos.Y;
        float bottom = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
        Span<float> projection =
        [
            2f / (right - left), 0, 0, 0,
            0, 2f / (top - bottom), 0, 0,
            0, 0, -1, 0,
            (right + left) / (left - right), (top + bottom) / (bottom - top), 0, 1
        ];

        _gl.UseProgram(_program);
        _gl.Uniform1(_textureLocation, 0);
        fixed (float* matrix = projection)
            _gl.UniformMatrix4(_projectionLocation, 1, false, matrix);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.ActiveTexture(TextureUnit.Texture0);

        Vector2 clipOffset = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;
        uint vertexSize = (uint)Unsafe.SizeOf<ImDrawVert>();

        for (int n = 0; n < drawData.CmdLists.Size; n++)
        {
            ImDrawListPtr commandList = drawData.CmdLists.Data[n];
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(commandList.VtxBuffer.Size * vertexSize),
                (void*)commandList.VtxBuffer.Data, BufferUsageARB.StreamDraw);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(commandList.IdxBuffer.Size * sizeof(ushort)),
                (void*)commandList.IdxBuffer.Data, BufferUsageARB.StreamDraw);

            for (int i = 0; i < commandList.CmdBuffer.Size; i++)
            {
                ref ImDrawCmd command = ref commandList.CmdBuffer.Data[i];
                if (command.UserCallback != null) continue;

                var clipMin = new Vector2((command.ClipRect.X - clipOffset.X) * clipScale.X,
                    (command.ClipRect.Y - clipOffset.Y) * clipScale.Y);
                var clipMax = new Vector2((command.ClipRect.Z - clipOffset.X) * clipScale.X,
                    (command.ClipRect.W - clipOffset.Y) * clipScale.Y);
                if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y) continue;

                _gl.Scissor((int)clipMin.X, (int)(framebufferHeight - clipMax.Y),
                    (uint)(clipMax.X - clipMin.X), (uint)(clipMax.Y - clipMin.Y));
                _gl.BindTexture(TextureTarget.Texture2D, (uint)command.GetTexID().Handle);
                _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, command.ElemCount, DrawElementsType.UnsignedShort,
                    (void*)(command.IdxOffset * sizeof(ushort)), (int)command.VtxOffset);
            }
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.Disable(EnableCap.ScissorTest);
    }

    private uint CreateProgram()
    {
        uint vertex = CompileShader(ShaderType.VertexShader, VertexSource);
        uint fragment = CompileShader(ShaderType.FragmentShader, FragmentSource);
        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        _gl.DetachShader(program, vertex);
        _gl.DetachShader(program, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        if (linked == 0)
        {
            var info = _gl.GetProgramInfoLog(program);
            _gl.DeleteProgram(program);
            throw new InvalidOperationException($"ImGui shader program failed to link: {info}");
        }
        return program;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            var info = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"ImGui {type} failed to compile: {info}");
        }
        return shader;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var texture in _staticTextures) _gl.DeleteTexture(texture);
        _staticTextures.Clear();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}
