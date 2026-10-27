using System.Numerics;
using Ashla.Host.Overlays;
using Ashla.SDK;
using Hexa.NET.ImGui;

namespace Ashla.Host.Gui;

internal sealed class ImGuiPanelChrome(Func<float> uiScale, Vector2 defaultOrigin) : IPanelChrome
{
    private readonly Dictionary<string, Vector2> _sizeBeforeFit = [];
    private readonly HashSet<string> _fitting = [];
    private string? _measure;

    public bool Begin(PanelEntry panel, string title, ref bool open)
    {
        float scale = uiScale();
        int slot = panel.Order % 6;
        var cell = new Vector2(slot % 2 * 440f, slot / 2 * 260f) + new Vector2(20f * (panel.Order / 6));
        ImGui.SetNextWindowPos((defaultOrigin + cell) * scale, ImGuiCond.FirstUseEver);

        var look = (panel.Panel as IPluginPanelAppearance)?.Appearance;
        var size = new Vector2(look?.DefaultWidth ?? 420f, look?.DefaultHeight ?? 240f);
        ImGui.SetNextWindowSize(size * scale, ImGuiCond.FirstUseEver);

        var flags = ImGuiWindowFlags.None;
        _measure = null;
        if (look is { FitToContent: true })
        {
            flags |= ImGuiWindowFlags.AlwaysAutoResize;
            _fitting.Add(panel.WindowId);
        }
        else if (_fitting.Remove(panel.WindowId) && _sizeBeforeFit.TryGetValue(panel.WindowId, out var before))
        {
            ImGui.SetNextWindowSize(before, ImGuiCond.Always);
        }
        else
        {
            _measure = panel.WindowId;
        }
        flags |= LookFlags(look);
        if (look?.BackgroundOpacity is { } opacity) ImGui.SetNextWindowBgAlpha(Math.Clamp(opacity, 0f, 1f));

        bool padded = look?.Padding is not null;
        if (padded) ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(look!.Padding!.Value) * scale);
        bool clear = look?.BackgroundOpacity is <= 0f;
        if (clear)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Vector4.Zero);
        }
        try
        {
            bool visible = look is { ShowTitleBar: false }
                ? ImGui.Begin($"{SanitizeTitle(title)}###{panel.WindowId}", flags)
                : ImGui.Begin($"{SanitizeTitle(title)}###{panel.WindowId}", ref open, flags);
            if (_measure is not null) _sizeBeforeFit[_measure] = ImGui.GetWindowSize();
            return visible;
        }
        finally
        {
            if (clear)
            {
                ImGui.PopStyleColor();
                ImGui.PopStyleVar();
            }
            if (padded) ImGui.PopStyleVar();
        }
    }

    public void End() => ImGui.End();

    internal static ImGuiWindowFlags LookFlags(PanelAppearance? look)
    {
        var flags = ImGuiWindowFlags.None;
        if (look is { ShowTitleBar: false }) flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse;
        if (look is { NoScrollbar: true }) flags |= ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (look is { ClickThrough: true }) flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus;
        return flags;
    }

    internal static string SanitizeTitle(string title)
    {
        while (title.Contains("##", StringComparison.Ordinal)) title = title.Replace("##", "#", StringComparison.Ordinal);
        return title;
    }
}
