using System.Numerics;
using Ashla.Rendering;
using Ashla.UI;
using Hexa.NET.ImGui;

namespace Ashla.Host.Gui;

internal static class LauncherChrome
{
    public const string HeadingFont = "Rajdhani-Bold";
    public const string LabelFont = "Rajdhani-SemiBold";
    public const string StrongFont = "Barlow-Medium";
    public const string TextFont = "Barlow-Regular";

    private static readonly string[] Glyphs = Enumerable.Range(0, 256).Select(i => ((char)i).ToString()).ToArray();
    private static readonly Vector2[] Points = new Vector2[6];
    private static ImGuiWindowHost? _window;

    public static float Scale { get; private set; } = 1f;

    public static Vector4 Deep => AshlaColors.Background.Shade(-0.38f);

    public static Vector4 Panel => AshlaColors.Background;

    public static Vector4 Line => AshlaColors.Border;

    public static Vector4 Frame => Vector4.Lerp(AshlaColors.Border, AshlaColors.AccentDim, 0.6f);

    public static Vector4 Gold => AshlaColors.Action;

    public static RenderWindowOptions WithFonts(RenderWindowOptions options)
    {
        var extra = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in new[] { LabelFont, StrongFont })
            if (ReadFont(name) is { } data) extra[name] = data;
        return options with { FontData = ReadFont(TextFont), LargeFontData = ReadFont(HeadingFont), ExtraFonts = extra };
    }

    internal static byte[]? ReadFont(string name)
    {
        using var stream = typeof(LauncherChrome).Assembly.GetManifestResourceStream($"Ashla.Fonts.{name}.ttf");
        if (stream is null) return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static void Begin(ImGuiWindowHost window)
    {
        _window = window;
        Scale = window.UiScale;
    }

    public static void ApplyStyle(ImGuiStylePtr style)
    {
        style.WindowRounding = 0f;
        style.ChildRounding = 0f;
        style.FrameRounding = 0f;
        style.PopupRounding = 0f;
        style.GrabRounding = 0f;
        style.TabRounding = 0f;
        style.ScrollbarRounding = 0f;
        style.FrameBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.TabBarBorderSize = 1f;
        style.TabBarOverlineSize = 2f;
        style.FramePadding = new Vector2(10f, 6f);
        style.ItemSpacing = new Vector2(8f, 7f);
        style.ItemInnerSpacing = new Vector2(8f, 6f);
        style.ScrollbarSize = 8f;
        style.GrabMinSize = 12f;

        Set(ImGuiCol.Border, Line);
        Set(ImGuiCol.FrameBg, Deep);
        Set(ImGuiCol.FrameBgHovered, Deep.Shade(0.06f));
        Set(ImGuiCol.FrameBgActive, AshlaColors.AccentDim.WithAlpha(0.4f));
        Set(ImGuiCol.PopupBg, Panel);
        Set(ImGuiCol.CheckMark, Gold);
        Set(ImGuiCol.SliderGrab, Gold.Shade(-0.2f));
        Set(ImGuiCol.SliderGrabActive, Gold);
        Set(ImGuiCol.Header, AshlaColors.AccentDim.WithAlpha(0.35f));
        Set(ImGuiCol.HeaderHovered, AshlaColors.AccentDim.WithAlpha(0.55f));
        Set(ImGuiCol.HeaderActive, AshlaColors.AccentDim.WithAlpha(0.75f));
        Set(ImGuiCol.Button, Deep);
        Set(ImGuiCol.ButtonHovered, AshlaColors.AccentDim.WithAlpha(0.45f));
        Set(ImGuiCol.ButtonActive, AshlaColors.AccentDim.WithAlpha(0.7f));
        Set(ImGuiCol.Tab, Vector4.Zero);
        Set(ImGuiCol.TabHovered, AshlaColors.AccentDim.WithAlpha(0.35f));
        Set(ImGuiCol.TabSelected, Deep);
        Set(ImGuiCol.TabSelectedOverline, Gold);
        Set(ImGuiCol.TabDimmedSelected, Deep);
        Set(ImGuiCol.Separator, Line);
        Set(ImGuiCol.ScrollbarGrab, Line);
        Set(ImGuiCol.ScrollbarGrabHovered, Frame);
        Set(ImGuiCol.ScrollbarGrabActive, AshlaColors.Accent);

        void Set(ImGuiCol slot, Vector4 color) => style.Colors[(int)slot] = color;
    }

    public static uint Fade(Vector4 color) => color.WithAlpha(color.W * ImGui.GetStyle().Alpha).U32();

    public static void PushFont(string? name, float size)
    {
        var font = _window is null ? default : name is null or HeadingFont ? _window.LargeFont : _window.ExtraFont(name);
        ImGui.PushFont(font, size);
    }

    public static float TrackedWidth(string text, float tracking)
    {
        float width = 0f;
        foreach (var c in text) width += ImGui.CalcTextSize(Glyph(c)).X;
        return width + tracking * ImGui.GetFontSize() * Math.Max(0, text.Length - 1);
    }

    public static void DrawTracked(ImDrawListPtr draw, Vector2 pos, uint color, string text, float tracking)
    {
        float gap = tracking * ImGui.GetFontSize();
        foreach (var c in text)
        {
            var glyph = Glyph(c);
            draw.AddText(new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y)), color, glyph);
            pos.X += ImGui.CalcTextSize(glyph).X + gap;
        }
    }

    private static string Glyph(char c) => c < Glyphs.Length ? Glyphs[c] : c.ToString();

    public static void Tracked(string text, string font, float size, Vector4 color, float tracking)
    {
        PushFont(font, size);
        var pos = ImGui.GetCursorScreenPos();
        float width = TrackedWidth(text, tracking);
        DrawTracked(ImGui.GetWindowDrawList(), pos, Fade(color), text, tracking);
        ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight()));
        ImGui.PopFont();
    }

    public static void Label(string text, Vector4? color = null, float size = 13f) =>
        Tracked(text.ToUpperInvariant(), LabelFont, size, color ?? Gold, 0.16f);

    public static void Heading(string text, float size = 28f)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var bar = new Vector2(36f, 3f) * Scale;
        draw.AddRectFilled(pos, pos + bar, Fade(Gold));
        ImGui.Dummy(bar);
        Tracked(text.ToUpperInvariant(), HeadingFont, size, AshlaColors.Text, 0.04f);
    }

    public static void Section(string text)
    {
        PushFont(LabelFont, 14f);
        string shown = text.ToUpperInvariant();
        var pos = ImGui.GetCursorScreenPos();
        float width = TrackedWidth(shown, 0.14f), height = ImGui.GetTextLineHeight();
        var draw = ImGui.GetWindowDrawList();
        DrawTracked(draw, pos, Fade(AshlaColors.Accent), shown, 0.14f);
        float right = pos.X + ImGui.GetContentRegionAvail().X;
        float start = pos.X + width + 10f * Scale;
        float y = MathF.Floor(pos.Y + height * 0.5f) + 0.5f;
        if (right > start) draw.AddLine(new Vector2(start, y), new Vector2(right, y), Fade(Line), 1f);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.PopFont();
    }

    public static void Chamfer(ImDrawListPtr draw, Vector2 min, Vector2 max, float cut, uint fill, uint outline = 0, float thickness = 1f)
    {
        cut = MathF.Min(cut, MathF.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
        if (fill != 0)
        {
            SetPoints(min, max, cut);
            draw.AddConvexPolyFilled(ref Points[0], Points.Length, fill);
        }
        if (outline != 0)
        {
            float inset = thickness * 0.5f;
            SetPoints(min + new Vector2(inset), max - new Vector2(inset), MathF.Max(0f, cut - inset * 0.4f));
            draw.AddPolyline(ref Points[0], Points.Length, outline, ImDrawFlags.Closed, thickness);
        }
    }

    private static void SetPoints(Vector2 min, Vector2 max, float cut)
    {
        Points[0] = min;
        Points[1] = new Vector2(max.X - cut, min.Y);
        Points[2] = new Vector2(max.X, min.Y + cut);
        Points[3] = max;
        Points[4] = new Vector2(min.X + cut, max.Y);
        Points[5] = new Vector2(min.X, max.Y - cut);
    }

    private static string Shown(string label)
    {
        int hidden = label.IndexOf("##", StringComparison.Ordinal);
        return (hidden >= 0 ? label[..hidden] : label).ToUpperInvariant();
    }

    public static float ButtonWidth(string label, float fontSize = 15f, float tracking = 0.08f)
    {
        float pad = ImGui.GetStyle().FramePadding.X * 1.4f;
        PushFont(LabelFont, fontSize);
        float width = TrackedWidth(Shown(label), tracking);
        ImGui.PopFont();
        return MathF.Ceiling(width + pad * 2f);
    }

    public static bool Button(string label, Vector2 size = default, bool primary = false, float fontSize = 15f, float tracking = 0.08f)
    {
        float frame = ImGui.GetFrameHeight();
        float width = size.X > 0 ? size.X
            : size.X < 0 ? MathF.Max(1f, ImGui.GetContentRegionAvail().X + size.X + 1f)
            : ButtonWidth(label, fontSize, tracking);
        float height = size.Y > 0 ? size.Y : frame;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        bool pressed = ImGui.InvisibleButton(label, new Vector2(width, height));
        bool hovered = ImGui.IsItemHovered(), held = ImGui.IsItemActive();

        var draw = ImGui.GetWindowDrawList();
        float cut = MathF.Min(MathF.Round(height * 0.3f), 12f * Scale);
        Vector4 text;
        if (primary)
        {
            var fill = held ? Gold.Shade(-0.14f) : hovered ? Gold.Shade(0.16f) : Gold;
            Chamfer(draw, min, max, cut, Fade(fill));
            text = AshlaColors.TextOn(Gold);
        }
        else
        {
            var fill = held ? AshlaColors.AccentDim.WithAlpha(0.45f) : hovered ? AshlaColors.AccentDim.WithAlpha(0.22f) : Deep.WithAlpha(0.6f);
            Chamfer(draw, min, max, cut, Fade(fill), Fade(hovered || held ? AshlaColors.Accent : Frame));
            text = AshlaColors.Text;
        }

        PushFont(LabelFont, fontSize);
        string shown = Shown(label);
        float textWidth = TrackedWidth(shown, tracking);
        float lineHeight = ImGui.GetTextLineHeight();
        var pos = new Vector2(MathF.Round(min.X + (width - textWidth) * 0.5f),
            MathF.Round(min.Y + (height - lineHeight) * 0.5f + lineHeight * 0.04f));
        draw.PushClipRect(min, max, true);
        DrawTracked(draw, pos, Fade(text), shown, tracking);
        draw.PopClipRect();
        ImGui.PopFont();
        return pressed;
    }

    public static void Status(Vector4 color, string title, string? detail)
    {
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        float line = ImGui.GetTextLineHeight();
        float size = MathF.Round(7f * Scale);
        var marker = new Vector2(origin.X + 1f, MathF.Round(origin.Y + (line - size) * 0.5f + 1f));
        draw.AddRectFilled(marker, marker + new Vector2(size), Fade(color));
        ImGui.Dummy(new Vector2(size + 11f * Scale, line));
        ImGui.SameLine(0, 0);
        ImGui.BeginGroup();
        PushFont(StrongFont, 16f);
        ImGui.TextWrapped(title);
        ImGui.PopFont();
        if (detail is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextMuted);
            ImGui.TextWrapped(detail);
            ImGui.PopStyleColor();
        }
        ImGui.EndGroup();
        ImGui.Dummy(new Vector2(0, ImGui.GetStyle().ItemSpacing.Y * 0.3f));
    }

    public static bool Radio(string label, bool active)
    {
        float line = ImGui.GetTextLineHeight();
        float box = MathF.Round(line * 0.95f);
        float gap = ImGui.GetStyle().ItemInnerSpacing.X;
        float width = box + gap + ImGui.CalcTextSize(label).X;
        float height = MathF.Max(box, line) + 2f * Scale;
        var min = ImGui.GetCursorScreenPos();
        bool pressed = ImGui.InvisibleButton(label, new Vector2(width, height));
        bool hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var boxMin = new Vector2(min.X, MathF.Round(min.Y + (height - box) * 0.5f));
        var boxMax = boxMin + new Vector2(box);
        draw.AddRectFilled(boxMin, boxMax, Fade(AshlaColors.Background));
        draw.AddRect(boxMin, boxMax, Fade(active ? Gold : hovered ? AshlaColors.Accent : Frame));
        if (active)
        {
            float inset = MathF.Round(box * 0.28f);
            draw.AddRectFilled(boxMin + new Vector2(inset), boxMax - new Vector2(inset), Fade(Gold));
        }
        draw.AddText(new Vector2(boxMax.X + gap, MathF.Round(min.Y + (height - line) * 0.5f)),
            Fade(active || hovered ? AshlaColors.Text : AshlaColors.Text.WithAlpha(0.85f)), label);
        return pressed;
    }

    public static bool NavItem(string label, bool active)
    {
        float height = ImGui.GetFrameHeight() + 2f * Scale;
        float width = ImGui.GetContentRegionAvail().X;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);
        bool pressed = ImGui.InvisibleButton(label, new Vector2(width, height));
        bool hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        if (active) draw.AddRectFilled(min, max, Fade(AshlaColors.AccentDim.WithAlpha(0.22f)));
        else if (hovered) draw.AddRectFilled(min, max, Fade(AshlaColors.AccentDim.WithAlpha(0.12f)));
        if (active) draw.AddRectFilled(min, new Vector2(min.X + 2f * Scale, max.Y), Fade(Gold));

        PushFont(LabelFont, 15f);
        string shown = Shown(label);
        float lineHeight = ImGui.GetTextLineHeight();
        var pos = new Vector2(min.X + 14f * Scale, MathF.Round(min.Y + (height - lineHeight) * 0.5f + lineHeight * 0.04f));
        DrawTracked(draw, pos, Fade(active || hovered ? AshlaColors.Text : AshlaColors.TextMuted), shown, 0.12f);
        ImGui.PopFont();
        return pressed;
    }

    public static bool Tabs(string id, IReadOnlyList<string> labels, ref int selected)
    {
        bool changed = false;
        var draw = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        float full = ImGui.GetContentRegionAvail().X;
        PushFont(LabelFont, 15f);
        float lineHeight = ImGui.GetTextLineHeight();
        float padX = 12f * Scale, height = lineHeight + 12f * Scale;
        ImGui.PushID(id);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        for (int i = 0; i < labels.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            string shown = labels[i].ToUpperInvariant();
            float width = TrackedWidth(shown, 0.12f) + padX * 2f;
            var min = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##tab{i}", new Vector2(width, height)) && selected != i)
            {
                selected = i;
                changed = true;
            }
            bool active = selected == i, hovered = ImGui.IsItemHovered();
            if (active) draw.AddRectFilled(new Vector2(min.X, min.Y + height - 2f * Scale), new Vector2(min.X + width, min.Y + height), Fade(Gold));
            DrawTracked(draw, new Vector2(min.X + padX, MathF.Round(min.Y + (height - lineHeight) * 0.5f)),
                Fade(active || hovered ? AshlaColors.Text : AshlaColors.TextMuted), shown, 0.12f);
        }
        ImGui.PopStyleVar();
        ImGui.PopID();
        ImGui.PopFont();
        float y = MathF.Floor(start.Y + height) - 0.5f;
        draw.AddLine(new Vector2(start.X, y), new Vector2(start.X + full, y), Fade(Line));
        ImGui.Dummy(new Vector2(0, 2f * Scale));
        return changed;
    }

    public static void FrameItem(float cut, Vector4? color = null)
    {
        Chamfer(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), cut * Scale, 0, Fade(color ?? Line));
    }
}
