using System.Diagnostics;
using System.Numerics;
using Ashla.Host.Overlay;
using Ashla.Host.Overlays;
using Ashla.Host.Settings;
using Ashla.Rendering;
using Ashla.SDK;
using Ashla.UI;
using Hexa.NET.ImGui;

namespace Ashla.Host.Gui;

internal sealed record GuiServices(
    string Version,
    string PluginsDirectory,
    string ConfigDirectory,
    CombatLogStatus? LogStatus = null,
    string? TrayIconPath = null,
    Hotkey? ToggleHotkey = null,
    ToastCenter? Toasts = null,
    ToastCorner ToastCorner = ToastCorner.BottomRight,
    AshlaSettingsStore? Settings = null,
    EncounterArchive? Archive = null,
    string? CombatLogFolder = null);

internal sealed class OverlayToggle
{
    public bool HiddenByUser { get; private set; }

    public Hotkey? Hotkey { get; set; }

    public void Toggle() => HiddenByUser = !HiddenByUser;
}

internal static class GuiHost
{
    public static int Run(RenderWindowOptions options, PanelRegistry panels, PluginManager plugins,
        ILogger log, Action onShutdown, GuiServices services, IGameWindowSource? overlaySource = null,
        CancellationToken stop = default)
    {
        bool overlay = overlaySource is not null;
        using var window = new ImGuiWindowHost(
            options with { Overlay = overlay, ConfigureStyle = () => AshlaTheme.Apply(ImGui.GetStyle()) },
            message => log.Info(message));
        var chrome = new ImGuiPanelChrome(() => window.UiScale, overlay ? new Vector2(380f, 40f) : new Vector2(60f, 80f));
        var manager = new PluginManagerWindow(plugins, panels, () => window.UiScale)
        {
            IsOpen = !overlay,
            DefaultPosition = overlay ? new Vector2(380f, 560f) : new Vector2(60f, 430f)
        };
        var toggle = new OverlayToggle();
        var tracker = overlay ? new OverlayTracker(window, overlaySource!, toggle, log) : null;
        var logWindow = new LogWindow(() => window.UiScale);
        var tray = overlay ? new TrayController(window, services, toggle, log) : null;
        var toasts = services.Toasts is { } center
            ? new ToastRenderer(center, () => services.ToastCorner, () => window.UiScale)
            : null;
        if (services.Toasts is { } toastCenter && services.LogStatus is { } logStatus)
            CombatLogToasts.Watch(logStatus, toastCenter);
        bool showDemo = false;
        int frameErrors = 0;

        window.BeforeFrame += () =>
        {
            if (stop.IsCancellationRequested) window.RequestClose();
            if (AshlaTheme.StyleIsStale) window.RefreshStyle();
            tracker?.Update();
            tray?.Update(tracker!.Found);
        };
        window.Frame += () =>
        {
            AshlaFonts.SetLarge(window.LargeFont, window.LargeFontSize);
            if (overlay) DrawLauncher(window, panels, manager, logWindow, tracker!, services, toggle);
            else DrawMainMenu(window, panels, manager, logWindow, ref showDemo);
            panels.RenderFrame(chrome);
            manager.Draw();
            logWindow.Draw();
            if (showDemo) ImGui.ShowDemoWindow(ref showDemo);
            toasts?.Draw();
        };
        window.FrameError += error =>
        {
            if (frameErrors++ < 5) log.Error("Host UI frame failed", error);
        };
        window.Shutdown += () =>
        {
            try { onShutdown(); }
            catch (Exception error) { log.Error("Shutdown failed", error); }
            tray?.Dispose();
        };

        log.Info(overlay
            ? $"Starting overlay host (Dear ImGui {ImGuiWindowHost.ImGuiVersion}); following {overlaySource!.Description}."
            : $"Starting graphical host (Dear ImGui {ImGuiWindowHost.ImGuiVersion}). SWTOR is not required.");
        try
        {
            window.Run();
        }
        catch (Exception error)
        {
            log.Error("The graphical host could not run. It needs a desktop session and an OpenGL 3.3 capable " +
                      "graphics driver (remote desktop sessions and VMs without GPU drivers may not provide one).", error);
            return 1;
        }
        log.Info("Graphical host closed.");
        return 0;
    }

    private static void DrawMainMenu(ImGuiWindowHost window, PanelRegistry panels, PluginManagerWindow manager,
        LogWindow logWindow, ref bool showDemo)
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu("Ashla"))
        {
            bool managerOpen = manager.IsOpen;
            if (ImGui.MenuItem("Plugin Manager", "", ref managerOpen)) manager.IsOpen = managerOpen;
            bool logOpen = logWindow.IsOpen;
            if (ImGui.MenuItem("Log", "", ref logOpen)) logWindow.IsOpen = logOpen;
            ImGui.MenuItem("Dear ImGui Demo", "", ref showDemo);
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) window.RequestClose();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Panels"))
        {
            DrawPanelToggles(panels, asMenuItems: true);
            ImGui.EndMenu();
        }

        ImGui.TextDisabled("External mode: no game data, SWTOR not required");
        ImGui.EndMainMenuBar();
    }

    private static void DrawLauncher(ImGuiWindowHost window, PanelRegistry panels, PluginManagerWindow manager,
        LogWindow logWindow, OverlayTracker tracker, GuiServices services, OverlayToggle toggle)
    {
        float scale = window.UiScale;
        ImGui.SetNextWindowPos(new Vector2(16f, 16f) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowCollapsed(true, ImGuiCond.Once);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
                                       ImGuiWindowFlags.NoDocking;
        if (ImGui.Begin("Ashla###ashla.launcher", flags))
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 18f);
            ImGui.TextDisabled($"Ashla {services.Version}  ·  following {tracker.Description}");
            if (toggle.Hotkey is { } hotkey) ImGui.TextDisabled($"{hotkey} hides or shows Ashla.");
            DrawCombatLogStatus(services.LogStatus);
            ImGui.PopTextWrapPos();
            bool managerOpen = manager.IsOpen;
            if (ImGui.Checkbox("Plugin Manager", ref managerOpen)) manager.IsOpen = managerOpen;
            ImGui.SameLine();
            bool logOpen = logWindow.IsOpen;
            if (ImGui.Checkbox("Log", ref logOpen)) logWindow.IsOpen = logOpen;
            ImGui.SeparatorText("Panels");
            DrawPanelToggles(panels, asMenuItems: false);
            if (services.Settings is { } settings) DrawThemeChoice(settings);
            DrawOpenLog(window, services);
            ImGui.Separator();
            if (ImGui.Button("Exit Ashla")) window.RequestClose();
            ImGui.SameLine();
            DrawSupportButton();
        }
        ImGui.End();
    }

    private static int _importing;

    private static void DrawOpenLog(ImGuiWindowHost window, GuiServices services)
    {
        if (!OperatingSystem.IsWindows() || services.Archive is not { } archive) return;
        ImGui.SeparatorText("Fights");
        bool busy = Volatile.Read(ref _importing) != 0;
        ImGui.BeginDisabled(busy);
        bool open = ImGui.Button(busy ? "Opening..." : "Open combat log...");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Adds the fights from an older SWTOR combat log to the Combat Meter's saved fights");
        if (!open || NativeFileDialog.PickFile(window.NativeHandle, "Open a SWTOR combat log", "Combat logs (combat_*.txt)",
                "combat_*.txt;*.txt", services.CombatLogFolder) is not { } path)
            return;
        Interlocked.Exchange(ref _importing, 1);
        _ = Task.Run(() =>
        {
            var file = Path.GetFileName(path);
            try
            {
                var result = archive.Import(path);
                services.Toasts?.Show(result.Found == 0 ? "Nothing to add" : "Combat log opened", result.Describe(file) +
                    (result.Added > 0 ? " Pick them from the Combat Meter's encounter list." : ""),
                    result.Found == 0 ? ToastKind.Warning : ToastKind.Success);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                services.Toasts?.Show("Couldn't open the combat log", $"{file}: {error.Message}", ToastKind.Error);
            }
            finally
            {
                Interlocked.Exchange(ref _importing, 0);
            }
        });
    }

    private static void DrawSupportButton()
    {
        const string label = "Support on Ko-fi";
        var style = ImGui.GetStyle();
        float width = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2;
        float available = ImGui.GetContentRegionAvail().X;
        if (available > width) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + available - width);
        var color = AshlaColors.Action;
        ImGui.PushStyleColor(ImGuiCol.Button, color.WithAlpha(0.85f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color.Shade(0.15f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, color.Shade(-0.15f));
        ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextOn(color));
        if (ImGui.Button(label)) LauncherWindow.ShellOpen(LauncherWindow.SupportUrl);
        ImGui.PopStyleColor(4);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Opens {LauncherWindow.SupportUrl} in your browser");
    }

    private static void DrawThemeChoice(AshlaSettingsStore settings)
    {
        ImGui.SeparatorText("Theme");
        var appearance = settings.Current.Appearance;
        static string Label(ThemeChoice choice, Faction? faction) => choice == ThemeChoice.Auto
            ? $"Auto ({ThemeSelection.Resolve(choice, faction).Name})"
            : ThemeSelection.Resolve(choice, null).Name;
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12f);
        bool open = ImGui.BeginCombo("##theme", Label(appearance.Theme, appearance.LastFaction));
        if (!open && ImGui.IsItemHovered()) ImGui.SetTooltip("Auto follows your character: Republic or Empire");
        if (!open) return;
        foreach (var choice in Enum.GetValues<ThemeChoice>())
        {
            if (!ImGui.Selectable(Label(choice, appearance.LastFaction), appearance.Theme == choice)) continue;
            appearance.Theme = choice;
            settings.Save();
            ThemeSelection.Apply(appearance);
        }
        ImGui.EndCombo();
    }

    private static void DrawCombatLogStatus(CombatLogStatus? status)
    {
        if (status is null) return;
        if (status.DirectoryExists == false)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.Warning);
            ImGui.TextWrapped("Combat logging looks off: no CombatLogs folder yet. In SWTOR, open Preferences > " +
                              "Combat Logging and enable it; Ashla picks it up automatically.");
            ImGui.PopStyleColor();
            return;
        }
        if (status.ActiveFile is not { } file) return;
        var last = status.LastLine is { } at ? $"last line {FormatAge(DateTime.Now - at)} ago" : "no new lines yet";
        ImGui.TextWrapped($"Combat log: {Path.GetFileName(file)}  ·  {last}");
    }

    private static string FormatAge(TimeSpan age) => age.TotalSeconds < 60
        ? $"{Math.Max(0, (int)age.TotalSeconds)}s"
        : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m" : $"{(int)age.TotalHours}h";

    private static void DrawPanelToggles(PanelRegistry panels, bool asMenuItems)
    {
        var all = panels.Snapshot();
        if (all.Count == 0) ImGui.TextDisabled("No plugin panels registered");
        foreach (var panel in all)
        {
            bool visible = panel.IsVisible;
            var label = $"{ImGuiPanelChrome.SanitizeTitle(panel.LastTitle)}##{panel.WindowId}";
            bool changed = asMenuItems
                ? ImGui.MenuItem(label, panel.Owner.Name, ref visible, panel.Fault is null)
                : ImGui.Checkbox(label, ref visible);
            if (changed) panel.IsVisible = visible;
        }
    }

    private sealed class OverlayTracker(ImGuiWindowHost window, IGameWindowSource source, OverlayToggle toggle, ILogger log)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly OverlayVisibilityPolicy _policy = new();
        private bool? _wasFound;
        private bool _gameWasForeground;

        public string Description => source.Description;

        public bool Found => _wasFound == true;

        public void Update()
        {
            var state = source.Poll(window.NativeHandle);
            if (state.Found != _wasFound)
            {
                log.Info(state.Found
                    ? $"Game window found ({state.ClientBounds.Width}x{state.ClientBounds.Height}); overlay attached."
                    : $"Waiting for the game window ({source.Description}). The overlay stays hidden until it appears.");
                _wasFound = state.Found;
            }

            if (state.Found && !state.ClientBounds.IsEmpty)
            {
                var b = state.ClientBounds;
                window.SetBounds(b.X, b.Y, b.Width, b.Height);
            }

            bool shownByGame = _policy.Update(state, _clock.Elapsed);
            window.SetShown(shownByGame && !toggle.HiddenByUser);

            if (state.GameIsForeground && !_gameWasForeground) window.BringToFront();
            _gameWasForeground = state.GameIsForeground;
        }
    }

    private sealed class TrayController(ImGuiWindowHost window, GuiServices services, OverlayToggle toggle, ILogger log)
        : IDisposable
    {
        private object? _icon;
        private bool _failed;
        private bool _notifiedLoggingOff;

        public void Update(bool gameFound)
        {
            if (!OperatingSystem.IsWindows() || _failed) return;
            if (_icon is null && !TryCreate()) return;
            var icon = (TrayIcon)_icon!;

            bool loggingOff = services.LogStatus?.DirectoryExists == false;
            icon.SetStatus(toggle.HiddenByUser
                ? toggle.Hotkey is { } hotkey ? $"hidden ({hotkey} shows it)" : "hidden"
                : loggingOff ? "combat logging is off in SWTOR"
                : gameFound ? "attached to SWTOR"
                : "waiting for SWTOR");
            if (loggingOff && !_notifiedLoggingOff)
            {
                _notifiedLoggingOff = true;
                icon.ShowNotification("Turn on combat logging",
                    "In SWTOR, open Preferences > Combat Logging and enable it. Ashla picks it up automatically.",
                    warning: true);
            }
        }

        private bool TryCreate()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                var icon = new TrayIcon("Ashla: starting", services.TrayIconPath);
                icon.Command += OnCommand;
                _icon = icon;
                RegisterHotkey(icon);
                icon.ShowNotification("Ashla is running",
                    toggle.Hotkey is { } hotkey
                        ? $"It appears over SWTOR while the game is focused. {hotkey} hides or shows it."
                        : "It appears over SWTOR while the game is focused. Right-click this icon for options.");
                return true;
            }
            catch (Exception error)
            {
                _failed = true;
                log.Warn($"Tray icon unavailable ({error.Message}). Use Exit in the overlay or end the process to quit.");
                return false;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void RegisterHotkey(TrayIcon icon)
        {
            if (services.ToggleHotkey is not { } hotkey) return;
            var problem = icon.SetToggleHotkey((uint)hotkey.Modifiers, hotkey.Key.VirtualKey, hotkey.ToString());
            if (problem is null)
            {
                toggle.Hotkey = hotkey;
                log.Info($"Hotkey {hotkey} hides or shows the overlay.");
                return;
            }
            log.Warn($"Hotkey not available: {problem} Choose another in the launcher's Settings > Overlay.");
            services.Toasts?.Show("Hotkey not available",
                $"{problem} Pick another in Settings > Overlay, or hide Ashla from its tray icon.", ToastKind.Warning);
            icon.ShowNotification("Hotkey not available",
                $"{problem} Pick another in Settings > Overlay. You can still hide Ashla from this icon's menu.",
                warning: true);
        }

        private void OnCommand(TrayCommand command)
        {
            switch (command)
            {
                case TrayCommand.ToggleVisibility:
                    toggle.Toggle();
                    log.Info(toggle.HiddenByUser ? "Overlay hidden." : "Overlay shown.");
                    break;
                case TrayCommand.OpenLogFile when LogHub.FilePath is { } file:
                    LogWindow.OpenFolder(file);
                    break;
                case TrayCommand.OpenLogFolder when LogHub.Directory is { } folder:
                    LogWindow.OpenFolder(folder);
                    break;
                case TrayCommand.OpenPluginsFolder:
                    Directory.CreateDirectory(services.PluginsDirectory);
                    LogWindow.OpenFolder(services.PluginsDirectory);
                    break;
                case TrayCommand.OpenSettingsFolder:
                    Directory.CreateDirectory(services.ConfigDirectory);
                    LogWindow.OpenFolder(services.ConfigDirectory);
                    break;
                case TrayCommand.Quit:
                    window.RequestClose();
                    break;
            }
        }

        public void Dispose()
        {
            if (OperatingSystem.IsWindows() && _icon is TrayIcon icon) icon.Dispose();
            _icon = null;
        }
    }
}

internal static class CombatLogToasts
{
    public static void Watch(CombatLogStatus status, ToastCenter toasts)
    {
        var gate = new object();
        bool? last = null;
        void Changed(bool exists)
        {
            lock (gate)
            {
                if (last == exists) return;
                bool? previous = last;
                last = exists;
                if (!exists)
                    toasts.Show("Combat logging looks off",
                        "In SWTOR, open Preferences > Combat Logging and enable it. Ashla picks it up automatically.",
                        ToastKind.Warning);
                else if (previous == false)
                    toasts.Show("Combat logging found", "The combat meter will show your fights.", ToastKind.Success);
            }
        }
        status.DirectoryExistsChanged += Changed;
        if (status.DirectoryExists is { } now) Changed(now);
    }
}
