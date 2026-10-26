using System.Numerics;
using Ashla.Host.Settings;
using Ashla.SDK;
using Ashla.UI;
using Hexa.NET.ImGui;

namespace Ashla.Host.Gui;

internal sealed class ToastRenderer(ToastCenter center, Func<ToastCorner> corner, Func<float> uiScale)
{
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                           ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                                           ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking |
                                           ImGuiWindowFlags.AlwaysAutoResize;

    private long? _hovered;

    public static Vector4 KindColor(ToastKind kind) => kind switch
    {
        ToastKind.Success => AshlaColors.Healing,
        ToastKind.Warning => AshlaColors.Warning,
        ToastKind.Error => AshlaColors.Error,
        _ => AshlaColors.Accent
    };

    public void Draw()
    {
        var toasts = center.Update(ImGui.GetIO().DeltaTime, _hovered);
        _hovered = null;
        if (toasts.Count == 0) return;

        float scale = uiScale();
        var viewport = ImGui.GetMainViewport();
        var where = corner();
        bool right = where is ToastCorner.TopRight or ToastCorner.BottomRight;
        bool bottom = where is ToastCorner.BottomLeft or ToastCorner.BottomRight;
        float margin = 16f * scale, gap = 8f * scale, width = 320f * scale;
        var padding = new Vector2(14f, 10f) * scale;
        float offset = 0f;

        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            var toast = toasts[i];
            var position = new Vector2(
                right ? viewport.WorkPos.X + viewport.WorkSize.X - margin : viewport.WorkPos.X + margin,
                bottom ? viewport.WorkPos.Y + viewport.WorkSize.Y - margin - offset : viewport.WorkPos.Y + margin + offset);
            ImGui.SetNextWindowPos(position, ImGuiCond.Always, new Vector2(right ? 1f : 0f, bottom ? 1f : 0f));
            ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));

            var color = KindColor(toast.Kind);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, toast.Opacity);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding + new Vector2(4f * scale, 0f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, AshlaColors.Surface);
            ImGui.PushStyleColor(ImGuiCol.Border, color.WithAlpha(0.45f));
            if (ImGui.Begin($"##ashla.toast.{toast.Id}", Flags))
            {
                ImGuiP.BringWindowToDisplayFront(ImGuiP.GetCurrentWindow());
                var origin = ImGui.GetWindowPos();
                var size = ImGui.GetWindowSize();
                ImGui.GetWindowDrawList().AddRectFilled(origin, origin + new Vector2(4f * scale, size.Y),
                    color.U32(), 4f * scale, ImDrawFlags.RoundCornersLeft);

                float wrap = ImGui.GetCursorPosX() + width - padding.X * 2 - 4f * scale;
                ImGui.PushTextWrapPos(wrap);
                ImGui.TextColored(AshlaColors.Text, toast.Title);
                if (toast.Count > 1)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(AshlaColors.TextMuted, $"x{toast.Count}");
                }
                if (toast.Text is { } text) ImGui.TextColored(AshlaColors.TextMuted, text);
                ImGui.TextColored(color.WithAlpha(0.8f), toast.Source);
                ImGui.PopTextWrapPos();

                if (ImGui.IsWindowHovered())
                {
                    _hovered = toast.Id;
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) center.Dismiss(toast.Id);
                }
                offset += ImGui.GetWindowHeight() + gap;
            }
            ImGui.End();
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(4);
        }
    }
}
