using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;

namespace Ashla.UI;

public static class AshlaColors
{
    public static Vector4 Background => AshlaTheme.Current.Background;
    public static Vector4 Surface => AshlaTheme.Current.Surface;
    public static Vector4 SurfaceRaised => AshlaTheme.Current.SurfaceRaised;
    public static Vector4 Border => AshlaTheme.Current.Border;
    public static Vector4 Text => AshlaTheme.Current.Text;
    public static Vector4 TextMuted => AshlaTheme.Current.TextMuted;

    public static Vector4 Accent => AshlaTheme.Current.Accent;
    public static Vector4 AccentDim => AshlaTheme.Current.AccentDim;

    public static Vector4 Companion => AshlaTheme.Current.Companion;

    public static Vector4 Healing => AshlaTheme.Current.Healing;
    public static Vector4 Damage => AshlaTheme.Current.Damage;
    public static Vector4 DamageTaken => AshlaTheme.Current.DamageTaken;
    public static Vector4 Threat => AshlaTheme.Current.Threat;
    public static Vector4 Neutral => AshlaTheme.Current.Neutral;

    public static Vector4 Warning => AshlaTheme.Current.Warning;

    public static Vector4 Error => AshlaTheme.Current.Error;

    public static Vector4 Action => AshlaTheme.Current.Action;

    public static Vector4 TextOn(Vector4 background) =>
        0.2126f * background.X + 0.7152f * background.Y + 0.0722f * background.Z > 0.5f
            ? AshlaTheme.Current.Background
            : AshlaTheme.Current.Text;

    public static Vector4 Rgb(byte r, byte g, byte b, float alpha = 1f) => new(r / 255f, g / 255f, b / 255f, alpha);

    public static Vector4 Hex(string hex)
    {
        var value = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public static Vector4 WithAlpha(this Vector4 color, float alpha) => color with { W = alpha };

    public static Vector4 Shade(this Vector4 color, float amount)
    {
        var target = amount >= 0 ? Vector3.One : Vector3.Zero;
        var rgb = Vector3.Lerp(new Vector3(color.X, color.Y, color.Z), target, MathF.Abs(amount));
        return new Vector4(rgb, color.W);
    }

    public static uint U32(this Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
}

public static class AshlaFonts
{
    private static ImFontPtr _large;
    private static float _largeSize;

    public static bool HasLarge => !_large.IsNull && _largeSize > 0;

    public static ImFontPtr Large => _large;

    public static float LargeSize => _largeSize;

    public static float LargePixelSize
    {
        get
        {
            if (!HasLarge) return ImGui.GetFontSize();
            var style = ImGui.GetStyle();
            return _largeSize * style.FontScaleMain * style.FontScaleDpi;
        }
    }

    internal static void SetLarge(ImFontPtr font, float size)
    {
        _large = font;
        _largeSize = size;
    }

    public static void LargeText(string text, Vector4 color)
    {
        bool pushed = HasLarge;
        if (pushed) ImGui.PushFont(_large, _largeSize);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        if (pushed) ImGui.PopFont();
    }
}

public enum AshlaThemeId
{
    Ashla,
    Republic,
    Empire,
    Mandalorian
}

public sealed record AshlaPalette(
    AshlaThemeId Id,
    string Name,
    Vector4 Background,
    Vector4 Surface,
    Vector4 SurfaceRaised,
    Vector4 Border,
    Vector4 Text,
    Vector4 TextMuted,
    Vector4 Accent,
    Vector4 AccentDim,
    Vector4 Companion,
    Vector4 Healing,
    Vector4 Damage,
    Vector4 DamageTaken,
    Vector4 Threat,
    Vector4 Neutral,
    Vector4 Warning,
    Vector4 Error,
    Vector4 Action,
    float Rounding)
{
    public static AshlaPalette Ashla { get; } = new(AshlaThemeId.Ashla, "Ashla",
        Background: AshlaColors.Hex("#0E131B"), Surface: AshlaColors.Hex("#161E2A"),
        SurfaceRaised: AshlaColors.Hex("#1E2938"), Border: AshlaColors.Hex("#2A3748"),
        Text: AshlaColors.Hex("#E6EDF5"), TextMuted: AshlaColors.Hex("#8A97A8"),
        Accent: AshlaColors.Hex("#4FB3FF"), AccentDim: AshlaColors.Hex("#2B6CA3"),
        Companion: AshlaColors.Hex("#F2B544"), Healing: AshlaColors.Hex("#5BD18B"),
        Damage: AshlaColors.Hex("#FF8A4C"), DamageTaken: AshlaColors.Hex("#E5675C"),
        Threat: AshlaColors.Hex("#B98CF0"), Neutral: AshlaColors.Hex("#9AA7B8"),
        Warning: AshlaColors.Hex("#F2B544"), Error: AshlaColors.Hex("#EF5F55"), Action: AshlaColors.Hex("#F2B544"), Rounding: 1f);

    public static AshlaPalette Republic { get; } = new(AshlaThemeId.Republic, "Republic",
        Background: AshlaColors.Hex("#0A1424"), Surface: AshlaColors.Hex("#112038"),
        SurfaceRaised: AshlaColors.Hex("#18304F"), Border: AshlaColors.Hex("#2A4669"),
        Text: AshlaColors.Hex("#EEF3FA"), TextMuted: AshlaColors.Hex("#8EA2BC"),
        Accent: AshlaColors.Hex("#E2B04A"), AccentDim: AshlaColors.Hex("#8C6A27"),
        Companion: AshlaColors.Hex("#7CC4F5"), Healing: AshlaColors.Hex("#5FD394"),
        Damage: AshlaColors.Hex("#F59A52"), DamageTaken: AshlaColors.Hex("#E86A5E"),
        Threat: AshlaColors.Hex("#B595EE"), Neutral: AshlaColors.Hex("#9CB0C8"),
        Warning: AshlaColors.Hex("#F2963F"), Error: AshlaColors.Hex("#EC6155"), Action: AshlaColors.Hex("#E2B04A"), Rounding: 0.75f);

    public static AshlaPalette Empire { get; } = new(AshlaThemeId.Empire, "Empire",
        Background: AshlaColors.Hex("#0C0A0B"), Surface: AshlaColors.Hex("#181314"),
        SurfaceRaised: AshlaColors.Hex("#241B1D"), Border: AshlaColors.Hex("#3C2A2D"),
        Text: AshlaColors.Hex("#F0EAE9"), TextMuted: AshlaColors.Hex("#A1918E"),
        Accent: AshlaColors.Hex("#D63A3A"), AccentDim: AshlaColors.Hex("#7C2023"),
        Companion: AshlaColors.Hex("#A9B7C4"), Healing: AshlaColors.Hex("#62C98B"),
        Damage: AshlaColors.Hex("#F28A3E"), DamageTaken: AshlaColors.Hex("#F0A23E"),
        Threat: AshlaColors.Hex("#C08FE0"), Neutral: AshlaColors.Hex("#958A8C"),
        Warning: AshlaColors.Hex("#E9A23B"), Error: AshlaColors.Hex("#FF5F52"), Action: AshlaColors.Hex("#C9302E"), Rounding: 0.3f);

    public static AshlaPalette Mandalorian { get; } = new(AshlaThemeId.Mandalorian, "Mandalorian",
        Background: AshlaColors.Hex("#0F1213"), Surface: AshlaColors.Hex("#191E20"),
        SurfaceRaised: AshlaColors.Hex("#242B2E"), Border: AshlaColors.Hex("#384246"),
        Text: AshlaColors.Hex("#E9EDEE"), TextMuted: AshlaColors.Hex("#8E999C"),
        Accent: AshlaColors.Hex("#BCC8CE"), AccentDim: AshlaColors.Hex("#56646A"),
        Companion: AshlaColors.Hex("#C0533F"), Healing: AshlaColors.Hex("#6CC596"),
        Damage: AshlaColors.Hex("#E8964F"), DamageTaken: AshlaColors.Hex("#D8604F"),
        Threat: AshlaColors.Hex("#A993D6"), Neutral: AshlaColors.Hex("#919C9F"),
        Warning: AshlaColors.Hex("#D9A441"), Error: AshlaColors.Hex("#E2584C"), Action: AshlaColors.Hex("#BCC8CE"), Rounding: 0.25f);

    public static IReadOnlyList<AshlaPalette> All { get; } = [Ashla, Republic, Empire, Mandalorian];

    public static AshlaPalette For(AshlaThemeId id) => All.FirstOrDefault(p => p.Id == id) ?? Ashla;
}

public static class AshlaTheme
{
    private static volatile AshlaPalette _current = AshlaPalette.Ashla;
    private static AshlaPalette? _applied;

    public static AshlaPalette Current => _current;

    public static float Round(float radius) => radius * _current.Rounding;

    public static void Use(AshlaPalette palette) => _current = palette ?? AshlaPalette.Ashla;

    public static bool StyleIsStale => !ReferenceEquals(_applied, _current);

    public static void Apply(ImGuiStylePtr style)
    {
        var theme = _current;
        _applied = theme;
        float r = theme.Rounding;
        style.WindowRounding = 8f * r;
        style.ChildRounding = 6f * r;
        style.FrameRounding = 5f * r;
        style.PopupRounding = 6f * r;
        style.GrabRounding = 5f * r;
        style.TabRounding = 5f * r;
        style.ScrollbarRounding = 8f * r;
        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.WindowPadding = new Vector2(10f, 10f);
        style.FramePadding = new Vector2(8f, 4f);
        style.ItemSpacing = new Vector2(8f, 6f);
        style.ScrollbarSize = 10f;
        style.WindowTitleAlign = new Vector2(0f, 0.5f);

        Set(ImGuiCol.Text, theme.Text);
        Set(ImGuiCol.TextDisabled, theme.TextMuted);
        Set(ImGuiCol.WindowBg, theme.Background.WithAlpha(0.96f));
        Set(ImGuiCol.ChildBg, theme.Background.WithAlpha(0f));
        Set(ImGuiCol.PopupBg, theme.Surface.WithAlpha(0.98f));
        Set(ImGuiCol.Border, theme.Border.WithAlpha(0.7f));
        Set(ImGuiCol.BorderShadow, Vector4.Zero);
        Set(ImGuiCol.FrameBg, theme.Surface);
        Set(ImGuiCol.FrameBgHovered, theme.SurfaceRaised);
        Set(ImGuiCol.FrameBgActive, theme.AccentDim.WithAlpha(0.6f));
        Set(ImGuiCol.TitleBg, theme.Background);
        Set(ImGuiCol.TitleBgActive, theme.Surface);
        Set(ImGuiCol.TitleBgCollapsed, theme.Background.WithAlpha(0.8f));
        Set(ImGuiCol.MenuBarBg, theme.Background);
        Set(ImGuiCol.ScrollbarBg, Vector4.Zero);
        Set(ImGuiCol.ScrollbarGrab, theme.Border);
        Set(ImGuiCol.ScrollbarGrabHovered, theme.AccentDim);
        Set(ImGuiCol.ScrollbarGrabActive, theme.Accent);
        Set(ImGuiCol.CheckMark, theme.Accent);
        Set(ImGuiCol.SliderGrab, theme.AccentDim);
        Set(ImGuiCol.SliderGrabActive, theme.Accent);
        Set(ImGuiCol.Button, theme.SurfaceRaised);
        Set(ImGuiCol.ButtonHovered, theme.AccentDim);
        Set(ImGuiCol.ButtonActive, theme.Accent.Shade(-0.2f));
        Set(ImGuiCol.Header, theme.SurfaceRaised);
        Set(ImGuiCol.HeaderHovered, theme.AccentDim.WithAlpha(0.8f));
        Set(ImGuiCol.HeaderActive, theme.AccentDim);
        Set(ImGuiCol.Separator, theme.Border);
        Set(ImGuiCol.SeparatorHovered, theme.AccentDim);
        Set(ImGuiCol.SeparatorActive, theme.Accent);
        Set(ImGuiCol.ResizeGrip, theme.Border.WithAlpha(0.4f));
        Set(ImGuiCol.ResizeGripHovered, theme.AccentDim);
        Set(ImGuiCol.ResizeGripActive, theme.Accent);
        Set(ImGuiCol.Tab, theme.Surface);
        Set(ImGuiCol.TabHovered, theme.AccentDim);
        Set(ImGuiCol.TabSelected, theme.SurfaceRaised);
        Set(ImGuiCol.TableHeaderBg, theme.Surface);
        Set(ImGuiCol.TableBorderStrong, theme.Border);
        Set(ImGuiCol.TableBorderLight, theme.Border.WithAlpha(0.5f));
        Set(ImGuiCol.TableRowBg, Vector4.Zero);
        Set(ImGuiCol.TableRowBgAlt, theme.Surface.WithAlpha(0.35f));
        Set(ImGuiCol.PlotHistogram, theme.Accent);
        Set(ImGuiCol.TextSelectedBg, theme.AccentDim.WithAlpha(0.5f));
        Set(ImGuiCol.NavCursor, theme.Accent);
        Set(ImGuiCol.DockingPreview, theme.Accent.WithAlpha(0.5f));

        void Set(ImGuiCol slot, Vector4 color) => style.Colors[(int)slot] = color;
    }
}

public static class AshlaDraw
{
    public static void MeterBar(string label, string value, float fraction, Vector4 color, float height = 0f)
    {
        var drawList = ImGui.GetWindowDrawList();
        float textHeight = ImGui.GetTextLineHeight();
        if (height <= 0) height = MathF.Round(textHeight * 1.5f);
        var min = ImGui.GetCursorScreenPos();
        float width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.Dummy(new Vector2(width, height));
        var max = min + new Vector2(width, height);
        float rounding = AshlaTheme.Round(MathF.Min(4f, height * 0.3f));

        drawList.AddRectFilled(min, max, AshlaColors.Surface.WithAlpha(0.85f).U32(), rounding);
        float fill = Math.Clamp(fraction, 0f, 1f) * width;
        if (fill >= 1f)
        {
            var fillMax = new Vector2(min.X + fill, max.Y);
            drawList.AddRectFilled(min, fillMax, color.Shade(-0.25f).WithAlpha(0.9f).U32(), rounding);
            drawList.AddRectFilled(min, new Vector2(fillMax.X, min.Y + height * 0.5f), color.WithAlpha(0.35f).U32(), rounding,
                ImDrawFlags.RoundCornersTop);
            drawList.AddRectFilled(min, new Vector2(min.X + MathF.Min(3f, fill), max.Y), color.U32(), rounding,
                ImDrawFlags.RoundCornersLeft);
        }

        float textY = min.Y + (height - textHeight) * 0.5f;
        float pad = MathF.Round(textHeight * 0.45f);
        var valueSize = ImGui.CalcTextSize(value);
        var valuePos = new Vector2(max.X - pad - valueSize.X, textY);
        float labelRoom = valuePos.X - pad * 1.5f - (min.X + pad);
        ShadowText(drawList, new Vector2(min.X + pad, textY), Ellipsize(label, labelRoom));
        ShadowText(drawList, valuePos, value);
    }

    public static string Ellipsize(string text, float maxWidth)
    {
        if (maxWidth <= 0) return string.Empty;
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;
        const string dots = "...";
        int low = 0, high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid].TrimEnd() + dots).X <= maxWidth) low = mid;
            else high = mid - 1;
        }
        return low == 0 ? dots : text[..low].TrimEnd() + dots;
    }

    public static bool Tabs(string id, IReadOnlyList<string> labels, ref int selected, Vector4 accent)
    {
        bool changed = false;
        var drawList = ImGui.GetWindowDrawList();
        var padding = new Vector2(MathF.Round(ImGui.GetTextLineHeight() * 0.6f), MathF.Round(ImGui.GetTextLineHeight() * 0.2f));
        var start = ImGui.GetCursorScreenPos();
        float height = ImGui.GetTextLineHeight() + padding.Y * 2;
        float total = 0;
        foreach (var label in labels) total += ImGui.CalcTextSize(label).X + padding.X * 2;
        float rounding = AshlaTheme.Round(height * 0.5f);
        drawList.AddRectFilled(start, start + new Vector2(total, height), AshlaColors.Surface.WithAlpha(0.9f).U32(), rounding);

        ImGui.PushID(id);
        for (int i = 0; i < labels.Count; i++)
        {
            if (i > 0) ImGui.SameLine(0, 0);
            var size = new Vector2(ImGui.CalcTextSize(labels[i]).X + padding.X * 2, height);
            var pos = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##tab{i}", size) && selected != i)
            {
                selected = i;
                changed = true;
            }
            bool active = selected == i;
            bool hovered = ImGui.IsItemHovered();
            if (active) drawList.AddRectFilled(pos, pos + size, accent.Shade(-0.35f).WithAlpha(0.95f).U32(), rounding);
            else if (hovered) drawList.AddRectFilled(pos, pos + size, AshlaColors.SurfaceRaised.U32(), rounding);
            var color = active ? AshlaColors.Text : hovered ? AshlaColors.Text.WithAlpha(0.9f) : AshlaColors.TextMuted;
            drawList.AddText(pos + padding, color.U32(), labels[i]);
        }
        ImGui.PopID();
        return changed;
    }

    public static void StatusDot(Vector4 color)
    {
        float size = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size * 0.7f, size));
        ImGui.GetWindowDrawList().AddCircleFilled(pos + new Vector2(size * 0.3f, size * 0.55f), size * 0.22f, color.U32());
    }

    public static void RightAlignedText(string text, Vector4 color)
    {
        float width = ImGui.CalcTextSize(text).X;
        ImGui.SameLine();
        float available = ImGui.GetContentRegionAvail().X;
        if (available > width) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + available - width);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void MutedText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextMuted);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static string Compact(double value)
    {
        var culture = CultureInfo.CurrentCulture;
        double abs = Math.Abs(value);
        return abs switch
        {
            >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.##", culture) + "B",
            >= 1_000_000 => (value / 1_000_000).ToString("0.##", culture) + "M",
            >= 10_000 => (value / 1_000).ToString("0.#", culture) + "k",
            _ => value.ToString("N0", culture)
        };
    }

    private static void ShadowText(ImDrawListPtr drawList, Vector2 pos, string text)
    {
        drawList.AddText(pos + new Vector2(1, 1), new Vector4(0, 0, 0, 0.7f).U32(), text);
        drawList.AddText(pos, AshlaColors.Text.U32(), text);
    }
}
