using System.Diagnostics;
using System.Numerics;
using Ashla.UI;
using Hexa.NET.ImGui;

namespace Ashla.Host.Gui;

internal sealed class LogWindow(Func<float> uiScale)
{
    private bool _open;
    private bool _showInfo = true;
    private bool _showWarn = true;
    private bool _showError = true;
    private long _seenVersion = -1;
    private LogEntry[] _entries = [];

    public bool IsOpen
    {
        get => _open;
        set => _open = value;
    }

    public void Draw()
    {
        if (!_open) return;
        float scale = uiScale();
        ImGui.SetNextWindowSize(new Vector2(720f, 320f) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new Vector2(380f, 360f) * scale, ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Log###ashla.log", ref _open)) DrawContents();
        ImGui.End();
    }

    private void DrawContents()
    {
        if (LogHub.Recent.Version != _seenVersion)
        {
            _seenVersion = LogHub.Recent.Version;
            _entries = LogHub.Recent.Snapshot();
        }

        ImGui.Checkbox("Info", ref _showInfo);
        ImGui.SameLine();
        ImGui.Checkbox("Warnings", ref _showWarn);
        ImGui.SameLine();
        ImGui.Checkbox("Errors", ref _showError);
        ImGui.SameLine();
        if (ImGui.Button("Copy")) ImGui.SetClipboardText(string.Join(Environment.NewLine, Visible().Select(e => e.Format())));
        if (LogHub.Directory is { } folder)
        {
            ImGui.SameLine();
            if (ImGui.Button("Open log folder")) OpenFolder(folder);
        }

        if (!ImGui.BeginChild("lines", Vector2.Zero, ImGuiChildFlags.Borders)) { ImGui.EndChild(); return; }
        bool atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;
        foreach (var entry in Visible())
        {
            var color = entry.Level switch
            {
                LogLevel.Error => AshlaColors.Error,
                LogLevel.Warn => AshlaColors.Warning,
                _ => AshlaColors.Text
            };
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(entry.Format());
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }
        if (atBottom) ImGui.SetScrollHereY(1f);
        ImGui.EndChild();
    }

    private IEnumerable<LogEntry> Visible() => _entries.Where(e => e.Level switch
    {
        LogLevel.Error => _showError,
        LogLevel.Warn => _showWarn,
        _ => _showInfo
    });

    internal static void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            LogHub.Write(LogLevel.Warn, "Host", $"Couldn't open {folder}: {error.Message}");
        }
    }
}
