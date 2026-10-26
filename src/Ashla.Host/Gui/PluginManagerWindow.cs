using System.Numerics;
using Ashla.Host.Overlays;
using Hexa.NET.ImGui;

namespace Ashla.Host.Gui;

internal sealed class PluginManagerWindow(PluginManager plugins, PanelRegistry panels, Func<float> uiScale)
{
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Red = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Grey = new(0.6f, 0.6f, 0.6f, 1f);

    private bool _open = true;

    public Vector2 DefaultPosition { get; init; } = new(520f, 80f);

    public bool IsOpen
    {
        get => _open;
        set => _open = value;
    }

    public void Draw()
    {
        if (!_open) return;
        float scale = uiScale();
        ImGui.SetNextWindowPos(DefaultPosition * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(700f, 300f) * scale, ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Plugin Manager###ashla.plugin-manager", ref _open))
            DrawContents();
        ImGui.End();
    }

    private void DrawContents()
    {
        var records = plugins.GetSnapshot();
        var panelsByOwner = panels.Snapshot().ToLookup(p => p.Owner.Id);

        ImGui.TextUnformatted($"{records.Count(r => r.Status == PluginStatus.Loaded)} of {records.Count} plugin(s) loaded.");
        ImGui.SameLine();
        Colored(Grey, "Enable/disable takes effect after restarting Ashla (no hot reload).");

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                                      ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("plugins", 6, flags)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.None, 1.4f);
        ImGui.TableSetupColumn("Version", ImGuiTableColumnFlags.None, 0.6f);
        ImGui.TableSetupColumn("Author", ImGuiTableColumnFlags.None, 0.8f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.None, 1.2f);
        ImGui.TableSetupColumn("Panels", ImGuiTableColumnFlags.None, 1.6f);
        ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.None, 0.5f);
        ImGui.TableHeadersRow();

        foreach (var record in records)
        {
            ImGui.PushID(record.Id);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(record.Name);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"Id: {record.Id}");
                if (record.Description is not null) ImGui.TextUnformatted(record.Description);
                ImGui.TextUnformatted(record.AssemblyPath);
                ImGui.EndTooltip();
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(record.Version);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(record.Author ?? "-");

            ImGui.TableNextColumn();
            DrawStatus(record);

            ImGui.TableNextColumn();
            DrawPanels(panelsByOwner[record.Id]);

            ImGui.TableNextColumn();
            bool enabled = record.EnabledOnNextStart;
            if (ImGui.Checkbox("##enabled", ref enabled))
                plugins.State.SetEnabled(record.Id, enabled);
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip(enabled ? "Loads on next start" : "Will not load on next start");

            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private static void DrawStatus(PluginRecord record)
    {
        var color = record.Status switch
        {
            PluginStatus.Loaded => Green,
            PluginStatus.Failed => Red,
            _ => Grey
        };
        Colored(color, record.Status.ToString());
        if (record.Error is not null) Wrapped(Red, record.Error);
    }

    private static void DrawPanels(IEnumerable<PanelEntry> owned)
    {
        bool any = false;
        foreach (var panel in owned)
        {
            any = true;
            bool visible = panel.IsVisible;
            if (ImGui.Checkbox($"{ImGuiPanelChrome.SanitizeTitle(panel.LastTitle)}##{panel.WindowId}", ref visible))
                panel.IsVisible = visible;

            if (panel.Fault is { } fault)
            {
                Wrapped(Red, $"Suspended: {fault.GetType().Name}: {fault.Message}");
                if (ImGui.SmallButton($"Retry##{panel.WindowId}")) panel.ClearFault();
            }
        }
        if (!any) Colored(Grey, "-");
    }

    private static void Colored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void Wrapped(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }
}
