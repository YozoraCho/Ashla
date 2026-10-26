using Ashla.SDK;
using Ashla.UI;
using Hexa.NET.ImGui;

namespace HelloWorldOverlay;

public sealed class HelloSettings
{
    public int Clicks { get; set; }
}

public sealed class HelloWorldOverlayPlugin : IAshlaPlugin
{
    private ILogger? _log;
    private IPanelRegistration? _registration;

    public string Name => "Hello World Overlay";

    public void Initialize(IPluginContext context)
    {
        _log = context.Log;
        if (!context.Overlays.IsAvailable)
        {
            _log.Info("[Hello World Overlay] No UI renderer in this mode; start Ashla with --gui to see the window.");
            return;
        }

        _registration = context.Overlays.Register(new HelloPanel(context.Plugin, context.Toasts, context.Config));
        _log.Info("[Hello World Overlay] Registered the \"Hello, SWTOR!\" panel.");
    }

    public void Dispose()
    {
        _registration?.Dispose();
        _registration = null;
        _log?.Info("[Hello World Overlay] Panel unregistered; unloading.");
    }

    private sealed class HelloPanel(PluginInfo plugin, IToasts toasts, IPluginConfig config) : IPluginPanel
    {
        private readonly HelloSettings _settings = config.Load<HelloSettings>();

        public string Id => "hello";
        public string Title => "Hello, SWTOR!";

        public void Draw()
        {
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.Accent);
            ImGui.TextUnformatted("Hello, SWTOR!");
            ImGui.PopStyleColor();
            ImGui.Separator();

            ImGui.TextUnformatted($"Plugin:  {plugin.Name}");
            ImGui.TextUnformatted($"Version: {plugin.Version}");
            if (plugin.Author is not null) ImGui.TextUnformatted($"Author:  {plugin.Author}");
            ImGui.Spacing();

            if (ImGui.Button("Click me"))
            {
                _settings.Clicks++;
                config.Save(_settings);
            }
            ImGui.SameLine();
            int clicks = _settings.Clicks;
            ImGui.TextUnformatted($"Clicked {clicks} time{(clicks == 1 ? "" : "s")}");

            if (ImGui.Button("Show a toast"))
                toasts.Show("Hello from a plugin", $"You've clicked {_settings.Clicks} time{(_settings.Clicks == 1 ? "" : "s")} so far.");
            ImGui.SameLine();
            if (ImGui.Button("Warning")) toasts.Show("Something to look at", kind: ToastKind.Warning);

            ImGui.Spacing();
            ImGui.TextDisabled("Drag the title bar to move; drag an edge or corner to resize.");
        }
    }
}
