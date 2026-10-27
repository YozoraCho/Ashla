using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Ashla.Host.Launch;
using Ashla.Host.Overlay;
using Ashla.Host.Settings;
using Ashla.Rendering;
using Ashla.SDK;
using Ashla.UI;
using Hexa.NET.ImGui;

namespace Ashla.Host.Gui;

internal sealed record LauncherFolders(string Plugins, string? Logs, string? CombatLogs = null);

internal static class LauncherWindow
{
    public const string SourceUrl = "https://github.com/YozoraCho/Ashla";
    public const string SupportUrl = "https://ko-fi.com/yozoracho";

    private const float SidebarWidth = 230f;
    private const ImGuiWindowFlags NoScroll = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    public static LauncherOutcome Run(LauncherModel model, string version, LauncherFolders folders, float? uiScale,
        IReadOnlyList<RenderImage> icons, ILogger log)
    {
        using var window = new ImGuiWindowHost(new RenderWindowOptions
        {
            Title = "Ashla",
            Width = 760,
            Height = 470,
            Resizable = false,
            CenterOnScreen = true,
            UiScaleOverride = uiScale,
            IniFilePath = null,
            ConfigureStyle = () => AshlaTheme.Apply(ImGui.GetStyle()),
            Icons = icons
        }, message => log.Info(message));

        var view = new View(model, version, folders, window, icons.Count > 0 ? icons[^1] : null);
        window.BeforeFrame += () =>
        {
            model.Refresh();
            if (AshlaTheme.StyleIsStale) window.RefreshStyle();
            if (model.Outcome is not null) window.RequestClose();
        };
        window.Frame += view.Draw;
        window.FrameError += error => log.Error("Launcher frame failed", error);

        try
        {
            window.Run();
        }
        catch (Exception error)
        {
            log.Error("The launcher window could not open. Ashla needs a desktop session and an OpenGL 3.3 capable " +
                      "graphics driver.", error);
            return LauncherOutcome.Quit;
        }
        return model.Outcome ?? LauncherOutcome.Quit;
    }

    internal static void ShellOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error)
        {
            LogHub.Write(LogLevel.Warn, "Launcher", $"Couldn't open {target}: {error.Message}");
        }
    }

    private sealed class View(LauncherModel model, string version, LauncherFolders folders, ImGuiWindowHost window,
        RenderImage? logo)
    {
        private const string SettingsPopup = "Settings###ashla.settings";
        private ImTextureRef? _logoTexture;
        private bool _confirmDeleteFights;

        public void Draw()
        {
            AshlaFonts.SetLarge(window.LargeFont, window.LargeFontSize);
            float scale = window.UiScale;
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos);
            ImGui.SetNextWindowSize(viewport.Size);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, AshlaColors.Background);
            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                           ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
                                           ImGuiWindowFlags.NoDocking;
            bool open = ImGui.Begin("##ashla.launcher", flags);
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(3);
            if (open && model.SetupDraft is { } setup)
            {
                DrawSetupSidebar(scale);
                ImGui.SameLine(0, 0);
                DrawSetup(setup, scale);
            }
            else if (open)
            {
                DrawSidebar(scale);
                ImGui.SameLine(0, 0);
                DrawMain(scale);
                DrawSettingsWindow(scale);
            }
            ImGui.End();
        }

        private void DrawSidebar(float scale)
        {
            BeginSidebar(scale);
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextMuted);
            ImGui.TextWrapped("Overlays and combat-log tools for Star Wars: The Old Republic.");
            ImGui.PopStyleColor();
            if (model.AshlaUpdate is { } release)
            {
                ImGui.Dummy(new Vector2(0, 12f * scale));
                ImGui.TextColored(AshlaColors.Accent, $"Ashla {release.Version} is out");
                ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextMuted);
                if (model.UpdatingAshla) ImGui.TextWrapped(model.UpdateStatus ?? "Updating...");
                else if (model.UpdateError is null)
                    ImGui.TextWrapped(model.CanUpdateInPlace
                        ? "Ashla downloads it, checks it and restarts. Your plugins and settings stay."
                        : "Extract it over this folder to keep your plugins.");
                ImGui.PopStyleColor();
                if (model.UpdateError is { } error)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.Error);
                    ImGui.TextWrapped(error);
                    ImGui.PopStyleColor();
                }
                ImGui.Spacing();
                ReleaseButtons(release, new Vector2(-1, 0), stacked: true);
            }
            EndSidebar();
        }

        private void BeginSidebar(float scale)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, AshlaColors.Surface);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22f, 26f) * scale);
            ImGui.BeginChild("sidebar", new Vector2(SidebarWidth * scale, 0), ImGuiChildFlags.AlwaysUseWindowPadding, NoScroll);
            ImGui.PopStyleVar();

            DrawLogo(72f * scale);
            ImGui.Spacing();
            AshlaFonts.LargeText("ASHLA", AshlaColors.Text);
            AshlaDraw.MutedText($"Version {version}");
            ImGui.Spacing();
        }

        private void EndSidebar()
        {
            float buttonHeight = ImGui.GetFrameHeight();
            float bottom = ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y - buttonHeight;
            if (ImGui.GetCursorPosY() < bottom) ImGui.SetCursorPosY(bottom);
            if (ImGui.Button("Quit", new Vector2(-1, 0))) model.Quit();

            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        private void DrawLogo(float size)
        {
            if (logo is not null) _logoTexture ??= window.CreateTexture(logo);
            if (_logoTexture is { } texture) ImGui.Image(texture, new Vector2(size, size));
            else DrawCrescent(size, AshlaColors.Surface);
        }

        private static void DrawCrescent(float size, Vector4 background)
        {
            var draw = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            float r = size * 0.5f;
            var center = origin + new Vector2(r, r);
            draw.AddCircleFilled(center, r, AshlaColors.AccentDim.WithAlpha(0.35f).U32(), 64);
            draw.AddCircle(center, r, AshlaColors.Accent.WithAlpha(0.6f).U32(), 64, MathF.Max(1f, size / 48f));
            var moon = center + new Vector2(-r * 0.08f, r * 0.04f);
            draw.AddCircleFilled(moon, r * 0.62f, AshlaColors.Accent.U32(), 64);
            draw.AddCircleFilled(moon + new Vector2(r * 0.30f, -r * 0.22f), r * 0.52f, background.U32(), 64);
            ImGui.Dummy(new Vector2(size, size));
        }

        private static string StepName(SetupStep step) => step switch
        {
            SetupStep.Welcome => "Welcome",
            SetupStep.Game => "SWTOR",
            SetupStep.CombatLogging => "Combat Logging",
            SetupStep.Hotkey => "Hotkey",
            SetupStep.Toasts => "Toasts",
            SetupStep.Theme => "Theme",
            SetupStep.Plugins => "Plugins",
            _ => "Finish"
        };

        private void DrawSetupSidebar(float scale)
        {
            BeginSidebar(scale);
            ImGui.Dummy(new Vector2(0, 6f * scale));
            var draw = ImGui.GetWindowDrawList();
            float row = ImGui.GetFrameHeight();
            float radius = row * 0.22f;
            foreach (var step in Enum.GetValues<SetupStep>())
            {
                bool current = step == model.SetupStep, done = step < model.SetupStep;
                var origin = ImGui.GetCursorScreenPos();
                var center = origin + new Vector2(radius + 1f, row * 0.5f);
                if (done)
                {
                    draw.AddCircleFilled(center, radius, AshlaColors.Healing.U32(), 20);
                }
                else
                {
                    draw.AddCircle(center, radius, (current ? AshlaColors.Accent : AshlaColors.TextMuted).U32(), 20,
                        MathF.Max(1f, 1.5f * scale));
                    if (current) draw.AddCircleFilled(center, radius * 0.5f, AshlaColors.Accent.U32(), 16);
                }
                ImGui.SetCursorScreenPos(origin + new Vector2(radius * 2f + 12f * scale,
                    (row - ImGui.GetTextLineHeight()) * 0.5f));
                ImGui.TextColored(current ? AshlaColors.Text : AshlaColors.TextMuted, StepName(step));
                ImGui.SetCursorScreenPos(origin + new Vector2(0, row + 2f * scale));
            }
            ImGui.Dummy(Vector2.Zero);
            EndSidebar();
        }

        private void DrawSetup(SettingsDraft draft, float scale)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28f, 24f) * scale);
            ImGui.BeginChild("setup", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding, NoScroll);
            ImGui.PopStyleVar();

            AshlaFonts.LargeText(model.SetupStep switch
            {
                SetupStep.Welcome => "Welcome to Ashla",
                SetupStep.Game => "Where's SWTOR?",
                SetupStep.CombatLogging => "Combat Logging",
                SetupStep.Hotkey => "Show/Hide Hotkey",
                SetupStep.Toasts => "Toasts",
                SetupStep.Theme => "Theme",
                SetupStep.Plugins => "Plugins",
                _ => "All Set"
            }, AshlaColors.Text);
            ImGui.Dummy(new Vector2(0, 2f * scale));

            float footer = ImGui.GetFrameHeight() + ImGui.GetTextLineHeightWithSpacing() * 2.2f +
                           ImGui.GetStyle().ItemSpacing.Y * 2;
            ImGui.PushStyleColor(ImGuiCol.FrameBg, AshlaColors.Surface);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, AshlaColors.SurfaceRaised);
            ImGui.BeginChild("page", new Vector2(0, -footer));
            switch (model.SetupStep)
            {
                case SetupStep.Welcome: DrawWelcome(); break;
                case SetupStep.Game: DrawGame(draft); break;
                case SetupStep.CombatLogging: DrawCombatLogging(); break;
                case SetupStep.Hotkey: DrawSetupHotkey(draft); break;
                case SetupStep.Toasts: DrawSetupToasts(draft, scale); break;
                case SetupStep.Theme: DrawSetupTheme(draft, scale); break;
                case SetupStep.Plugins: DrawSetupPlugins(draft); break;
                default: DrawSetupSummary(draft); break;
            }
            ImGui.EndChild();
            ImGui.PopStyleColor(2);
            DrawSetupFooter(draft, footer);
            ImGui.EndChild();
        }

        private static void DrawWelcome()
        {
            ImGui.TextWrapped("Ashla shows overlays, like a combat meter, on top of SWTOR while you play.");
            ImGui.Spacing();
            Muted("It runs beside the game and only reads the combat logs SWTOR writes. It never changes the game, " +
                  "reads its memory or presses keys for you.");
            ImGui.Spacing();
            ImGui.TextWrapped("A few quick steps: where SWTOR is installed, combat logging, a hotkey to hide Ashla, and " +
                              "which plugins you want.");
            Muted("You can change all of it later in Settings (the gear), or run this setup again from there.");
        }

        private void DrawCombatLogging()
        {
            if (model.CombatLogFolderExists)
            {
                Status(AshlaColors.Healing, "Combat logging is on", "Ashla found the logs SWTOR writes. Nothing to do here.");
                return;
            }
            Status(AshlaColors.Warning, "No combat logs yet",
                "SWTOR only writes them when combat logging is turned on. Plugins like the combat meter need them.");
            ImGui.TextWrapped("1.  In SWTOR, open Preferences.");
            ImGui.TextWrapped("2.  Under Combat Logging, turn on combat logging.");
            ImGui.TextWrapped("3.  If you were already playing, log out to character select and back in.");
            ImGui.Spacing();
            if (ImGui.Button("Check again")) model.CheckCombatLogging();
            ImGui.Spacing();
            Muted("You can also do this later: Ashla picks the logs up as soon as SWTOR writes the first one.");
        }

        private static void DrawSetupHotkey(SettingsDraft draft)
        {
            ImGui.TextWrapped("One key combination hides or shows Ashla, even while the game has focus. Keep the " +
                              "default or press Change to pick your own.");
            ImGui.Spacing();
            DrawHotkey(draft, inSettings: false);
        }

        private static void DrawSetupToasts(SettingsDraft draft, float scale)
        {
            ImGui.TextWrapped("Short notices, like combat logging being off or a plugin that couldn't load, pop up in " +
                              "a corner of the game and fade away. Pick a corner your game UI leaves free.");
            ImGui.Spacing();
            CornerPicker(draft, scale);
        }

        private void DrawSetupTheme(SettingsDraft draft, float scale)
        {
            ImGui.TextWrapped("Pick a look for Ashla's windows. Auto turns Republic or Empire to match the character " +
                              "you're playing.");
            ImGui.Spacing();
            ThemePicker(draft, scale);
        }

        private void DrawSetupPlugins(SettingsDraft draft)
        {
            ImGui.TextWrapped("Ashla's features are plugins, so you only get what you want. Tick the ones to install " +
                              "from Holocron, Ashla's reviewed plugin list. You can add or remove plugins any time in " +
                              "Settings > Plugins.");
            ImGui.Spacing();
            if (!model.StoreAvailable)
            {
                Muted("Installing plugins isn't available in this build.");
                return;
            }
            if (model.Fetching && !model.CatalogLoaded)
            {
                Muted("Getting the plugin list...");
                return;
            }
            var choices = model.SetupChoices;
            if (choices.Count == 0)
            {
                var problem = model.Catalog.Repositories.FirstOrDefault(r => r.Repository.Official)?.Problem;
                Status(AshlaColors.Warning, "No plugins to show",
                    problem ?? "Holocron's list is empty right now. Check Settings > Plugins later.");
                if (ImGui.Button("Try again")) model.RefreshCatalog();
                return;
            }
            foreach (var entry in choices)
            {
                var plugin = entry.Plugin;
                ImGui.PushID(plugin.Id);
                bool installed = model.IsInstalled(plugin.Id) || model.ResultOf(plugin.Id) is { Ok: true };
                if (installed || !entry.Compatible)
                {
                    bool fixedValue = installed;
                    ImGui.BeginDisabled();
                    ImGui.Checkbox(plugin.DisplayName, ref fixedValue);
                    ImGui.EndDisabled();
                }
                else
                {
                    bool picked = draft.PluginPicks.Contains(plugin.Id);
                    if (ImGui.Checkbox(plugin.DisplayName, ref picked))
                    {
                        if (picked) draft.PluginPicks.Add(plugin.Id);
                        else draft.PluginPicks.Remove(plugin.Id);
                    }
                }
                ImGui.SameLine();
                Muted(installed ? $"{plugin.Version}  ·  installed" : entry.Compatible ? plugin.Version : $"{plugin.Version}  ·  needs a newer Ashla");
                float indent = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
                ImGui.Indent(indent);
                ImGui.PushTextWrapPos(0f);
                if (plugin.Description is { } description) Muted(description);
                ImGui.PopTextWrapPos();
                ImGui.Unindent(indent);
                ImGui.PopID();
            }
            ImGui.Spacing();
            Muted("Nothing is ticked for you. Picked plugins are installed when you press Next.");
        }

        private void DrawSetupSummary(SettingsDraft draft)
        {
            var install = model.DraftInstallation(draft) ?? (draft.FolderIsAutomatic ? model.Installation : null);
            Status(install is null ? AshlaColors.Neutral : AshlaColors.Healing,
                install is null ? "SWTOR's folder isn't set" : install.IsSteam ? "SWTOR (Steam)" : "SWTOR",
                draft.Method switch
                {
                    GameLaunchMethod.Steam => "Play starts SWTOR through Steam.",
                    GameLaunchMethod.SwtorLauncher => "Play opens the SWTOR launcher.",
                    _ => "You start SWTOR yourself; Ashla appears over it."
                });
            Status(model.CombatLogFolderExists ? AshlaColors.Healing : AshlaColors.Warning,
                model.CombatLogFolderExists ? "Combat logging is on" : "Combat logging isn't on yet",
                model.CombatLogFolderExists ? null : "Turn it on in SWTOR's Preferences when you're in game.");
            Status(draft.ToggleHotkey is null ? AshlaColors.Neutral : AshlaColors.Healing,
                draft.ToggleHotkey is { } hotkey ? $"{hotkey} hides or shows Ashla" : "No show/hide hotkey", null);
            Status(AshlaColors.Healing, $"Theme: {ThemeLabel(draft.Theme)}",
                draft.Theme == ThemeChoice.Auto ? $"Right now: {model.ThemeFor(ThemeChoice.Auto).Name}" : null);
            Status(AshlaColors.Healing, $"Toasts appear in the {CornerName(draft.ToastCorner).ToLowerInvariant()} corner", null);
            DrawPickedPlugins(draft);
            Muted("Press Finish to save. Everything here is also in Settings.");
        }

        private void DrawPickedPlugins(SettingsDraft draft)
        {
            var picked = model.SetupChoices.Where(e => draft.PluginPicks.Contains(e.Plugin.Id)).ToList();
            if (picked.Count == 0)
            {
                Status(AshlaColors.Neutral, "No plugins picked", "Find them later in Settings > Plugins > Browse.");
                return;
            }
            foreach (var entry in picked)
            {
                var name = entry.Plugin.DisplayName;
                if (model.IsInstalling(entry.Plugin.Id)) Status(AshlaColors.Accent, $"Installing {name}...", null);
                else if (model.ResultOf(entry.Plugin.Id) is { Ok: false } failed) Status(AshlaColors.Error, $"{name} wasn't installed", failed.Message);
                else Status(AshlaColors.Healing, $"{name} is installed", null);
            }
        }

        private void DrawSetupFooter(SettingsDraft draft, float height)
        {
            var style = ImGui.GetStyle();
            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - style.WindowPadding.Y - height + style.ItemSpacing.Y);
            ImGui.PushTextWrapPos(0f);
            if (draft.Error is { } error) Message(false, error);
            else if (draft.Notice is { } notice) Message(true, notice);
            ImGui.PopTextWrapPos();

            bool last = model.SetupStep == SetupStep.Done, first = model.SetupStep == SetupStep.Welcome;
            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - style.WindowPadding.Y - ImGui.GetFrameHeight());
            if (!last && ImGui.Button(model.SetupIsRerun ? "Cancel" : "Skip setup")) model.SkipSetup();
            if (last) ImGui.Dummy(new Vector2(0, ImGui.GetFrameHeight()));
            bool waiting = last && model.Installing;
            string next = waiting ? "Installing..." : last ? "Finish" : first ? "Get started" : "Next";
            float nextWidth = MathF.Max(ImGui.CalcTextSize(next).X + style.FramePadding.X * 4, ImGui.GetFontSize() * 7f);
            float backWidth = ImGui.CalcTextSize("Back").X + style.FramePadding.X * 4;
            float right = ImGui.GetWindowWidth() - style.WindowPadding.X;
            if (!first)
            {
                ImGui.SameLine(right - nextWidth - style.ItemSpacing.X - backWidth);
                if (ImGui.Button("Back", new Vector2(backWidth, 0))) model.SetupBack();
            }
            ImGui.SameLine(right - nextWidth);
            ImGui.PushStyleColor(ImGuiCol.Button, AshlaColors.AccentDim);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AshlaColors.Accent.Shade(-0.1f));
            ImGui.BeginDisabled(waiting);
            if (ImGui.Button(next, new Vector2(nextWidth, 0))) model.SetupNext();
            ImGui.EndDisabled();
            ImGui.PopStyleColor(2);
        }

        private void DrawMain(float scale)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28f, 24f) * scale);
            ImGui.BeginChild("main", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding, NoScroll);
            ImGui.PopStyleVar();

            float gearSize = 30f * scale;
            var rowStart = ImGui.GetCursorPos();
            AshlaFonts.LargeText(model.CanPlay ? "Ready to play" : "Set up SWTOR first", AshlaColors.Text);
            var afterTitle = ImGui.GetCursorPos();
            ImGui.SetCursorPos(new Vector2(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - gearSize, rowStart.Y));
            if (GearButton(gearSize)) model.OpenSettings();
            ImGui.SetCursorPos(afterTitle);
            ImGui.Dummy(new Vector2(0, 4f * scale));

            float buttonHeight = 50f * scale;
            float messageHeight = ImGui.GetTextLineHeightWithSpacing() * 2.2f;
            float bottom = ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y - buttonHeight - messageHeight;
            ImGui.BeginChild("status", new Vector2(0, MathF.Max(bottom - ImGui.GetCursorPosY(), ImGui.GetFrameHeight())), ImGuiChildFlags.None,
                ImGuiWindowFlags.NoBackground);

            if (model.Installation is { } install)
                Status(AshlaColors.Healing, install.IsSteam ? "SWTOR found (Steam)" : "SWTOR found", install.Folder);
            else if (model.Method == GameLaunchMethod.SwtorLauncher)
                Status(AshlaColors.Warning, "SWTOR's folder isn't set",
                    model.SavedFolderProblem ?? "Open Settings (the gear) to choose it.");
            else
                Status(AshlaColors.Neutral, "SWTOR's folder isn't set", "Not needed for the way Play starts the game.");

            Status(model.GameRunning ? AshlaColors.Healing : AshlaColors.Accent, model.MethodDescription, null);

            if (model.CombatLogFolderExists)
                Status(AshlaColors.Healing, "Combat logging is on", "Ashla reads the logs SWTOR writes.");
            else
                Status(AshlaColors.Warning, "No combat logs yet",
                    "In SWTOR, open Preferences > Combat Logging and enable it. Plugins like the meter need it.");

            if (model.ToggleHotkey is { } hotkey)
                Status(AshlaColors.Healing, $"{hotkey} hides or shows Ashla", "Works while you play. Change it in Settings.");
            else
                Status(AshlaColors.Neutral, "No show/hide hotkey", "Set one in Settings, or use Ashla's tray icon.");

            if (model.StoreAvailable && !model.HasPlugins)
                Status(AshlaColors.Accent, "No plugins installed", "Ashla's features are plugins. Pick some in Settings > Plugins > Browse.");

            if (model.Updates.Count > 0)
            {
                var names = string.Join(", ", model.Updates.Select(u => $"{u.Name} {u.Entry.Plugin.Version}"));
                Status(AshlaColors.Accent, model.Updates.Count == 1 ? $"Update available: {names}" : $"Updates available: {names}",
                    "Install from Settings > Plugins.");
            }

            ImGui.EndChild();
            ImGui.SetCursorPosY(bottom);

            if (model.LastResult is { } result) Message(result.Ok, result.Message);
            else AshlaDraw.MutedText("Ashla only starts the game's own launcher. It never sees your login.");

            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y - buttonHeight);
            if (PlayButton(model.PlayLabel, buttonHeight, model.CanPlay && !model.UpdatingAshla)) model.Play();

            ImGui.EndChild();
        }

        private static bool GearButton(float size)
        {
            var origin = ImGui.GetCursorScreenPos();
            bool pressed = ImGui.InvisibleButton("##settings", new Vector2(size, size));
            bool hovered = ImGui.IsItemHovered();
            if (hovered) ImGui.SetTooltip("Settings");

            var draw = ImGui.GetWindowDrawList();
            var center = origin + new Vector2(size * 0.5f);
            if (hovered) draw.AddRectFilled(origin, origin + new Vector2(size), AshlaColors.SurfaceRaised.U32(), 6f);
            uint color = (hovered ? AshlaColors.Text : AshlaColors.TextMuted).U32();
            float outer = size * 0.40f, body = size * 0.30f, hole = size * 0.12f, toothHalf = size * 0.075f;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * MathF.PI / 4f;
                var along = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var across = new Vector2(-along.Y, along.X) * toothHalf;
                var inner = center + along * body * 0.9f;
                var tip = center + along * outer;
                draw.AddQuadFilled(inner - across, tip - across, tip + across, inner + across, color);
            }
            draw.AddCircleFilled(center, body, color, 32);
            draw.AddCircleFilled(center, hole, AshlaColors.Background.U32(), 24);
            return pressed;
        }

        private static bool PlayButton(string label, float height, bool enabled)
        {
            var action = AshlaColors.Action;
            ImGui.PushStyleColor(ImGuiCol.Button, enabled ? action : action.WithAlpha(0.35f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, enabled ? action.Shade(0.15f) : action.WithAlpha(0.35f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, action.Shade(-0.15f));
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextOn(action));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, AshlaTheme.Round(4f));
            bool pushedFont = AshlaFonts.HasLarge;
            if (pushedFont) ImGui.PushFont(AshlaFonts.Large, AshlaFonts.LargeSize * 0.8f);
            bool pressed = ImGui.Button(label.ToUpperInvariant() + "##play", new Vector2(-1, height));
            if (pushedFont) ImGui.PopFont();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
            return pressed;
        }

        private static void Status(Vector4 color, string title, string? detail)
        {
            AshlaDraw.StatusDot(color);
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextWrapped(title);
            if (detail is not null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextMuted);
                ImGui.TextWrapped(detail);
                ImGui.PopStyleColor();
            }
            ImGui.EndGroup();
            ImGui.Dummy(new Vector2(0, ImGui.GetStyle().ItemSpacing.Y));
        }

        private void ReleaseButtons(AshlaRelease release, Vector2 size, bool stacked)
        {
            ImGui.PushID(release.Version);
            bool first = true;
            void Next()
            {
                if (!first && !stacked) ImGui.SameLine();
                first = false;
            }
            ImGui.BeginDisabled(model.UpdatingAshla);
            if (model.CanUpdateInPlace)
            {
                Next();
                if (ImGui.Button(model.UpdatingAshla ? "Updating..." : model.UpdateError is null ? "Update now" : "Try again", size)) model.UpdateAshla();
            }
            if (!model.CanUpdateInPlace || model.UpdateError is not null)
            {
                Next();
                if (ImGui.Button(release.Download is null ? "Open the release" : "Download", size)) ShellOpen(release.Target);
            }
            ImGui.EndDisabled();
            if (release.Download is not null)
            {
                Next();
                if (ImGui.Button("What's new", size)) ShellOpen(release.Page);
            }
            ImGui.PopID();
        }

        private static void Muted(string text)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextMuted);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
        }

        private static void Message(bool ok, string text)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ok ? AshlaColors.Healing : AshlaColors.Error);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
        }

        private void DrawSettingsWindow(float scale)
        {
            if (model.Editing is null) return;
            if (!ImGui.IsPopupOpen(SettingsPopup)) ImGui.OpenPopup(SettingsPopup);

            var viewport = ImGui.GetMainViewport();
            var margin = new Vector2(28f, 24f) * scale;
            ImGui.SetNextWindowPos(viewport.Pos + margin);
            ImGui.SetNextWindowSize(viewport.Size - margin * 2);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, AshlaColors.Background);
            ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.6f));
            bool open = true;
            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                                           ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | NoScroll;
            if (ImGui.BeginPopupModal(SettingsPopup, ref open, flags))
            {
                var draft = model.Editing;
                bool wasCapturing = draft?.CapturingHotkey == true;
                if (draft is not null)
                {
                    DrawSections(draft, scale);
                    ImGui.SameLine();
                    DrawSectionContent(draft, scale);
                    DrawFooter(draft);
                }

                bool cancel = !open || (!wasCapturing && ImGui.IsKeyPressed(ImGuiKey.Escape));
                if (cancel) model.CancelSettings();
                if (model.Editing is null) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            else if (!open)
            {
                model.CancelSettings();
            }
            ImGui.PopStyleColor(2);
        }

        private float FooterHeight => ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;

        private void DrawSections(SettingsDraft draft, float scale)
        {
            ImGui.BeginChild("sections", new Vector2(150f * scale, -FooterHeight), ImGuiChildFlags.None, NoScroll);
            foreach (var section in Enum.GetValues<SettingsSection>())
                if (ImGui.Selectable(section.ToString(), draft.Section == section, ImGuiSelectableFlags.None,
                        new Vector2(0, ImGui.GetFrameHeight())))
                {
                    draft.Section = section;
                    draft.CapturingHotkey = false;
                }
            ImGui.EndChild();
        }

        private void DrawSectionContent(SettingsDraft draft, float scale)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, AshlaColors.Surface);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, AshlaColors.Background);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, AshlaColors.SurfaceRaised);
            ImGui.PushStyleColor(ImGuiCol.Button, AshlaColors.SurfaceRaised);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 14f) * scale);
            ImGui.BeginChild("section", new Vector2(0, -FooterHeight), ImGuiChildFlags.AlwaysUseWindowPadding);
            ImGui.PopStyleVar();
            switch (draft.Section)
            {
                case SettingsSection.Game: DrawGame(draft); break;
                case SettingsSection.Overlay: DrawOverlay(draft); break;
                case SettingsSection.Appearance: DrawAppearance(draft); break;
                case SettingsSection.Fights: DrawFights(draft); break;
                case SettingsSection.Plugins: DrawPlugins(draft); break;
                case SettingsSection.About: DrawAbout(); break;
            }
            ImGui.EndChild();
            ImGui.PopStyleColor(4);
        }

        private void DrawFooter(SettingsDraft draft)
        {
            ImGui.Dummy(new Vector2(0, ImGui.GetStyle().ItemSpacing.Y * 0.5f));
            var style = ImGui.GetStyle();
            float saveWidth = ImGui.CalcTextSize("Save").X + style.FramePadding.X * 4;
            float cancelWidth = ImGui.CalcTextSize("Cancel").X + style.FramePadding.X * 4;
            float buttonsWidth = saveWidth + cancelWidth + style.ItemSpacing.X;

            float textWidth = ImGui.GetContentRegionAvail().X - buttonsWidth - style.ItemSpacing.X;
            ImGui.BeginGroup();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
            if (draft.Error is { } error) Message(false, error);
            else if (draft.Notice is { } notice) Message(true, notice);
            else AshlaDraw.MutedText("Changes apply when you press Save.");
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();

            ImGui.SameLine(ImGui.GetWindowWidth() - style.WindowPadding.X - buttonsWidth);
            if (ImGui.Button("Cancel", new Vector2(cancelWidth, 0))) model.CancelSettings();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, AshlaColors.AccentDim);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AshlaColors.Accent.Shade(-0.1f));
            if (ImGui.Button("Save", new Vector2(saveWidth, 0))) model.SaveSettings();
            ImGui.PopStyleColor(2);
        }

        private void DrawGame(SettingsDraft draft)
        {
            ImGui.SeparatorText("SWTOR Folder");
            string folder = draft.FolderInput;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##folder", "Empty: find SWTOR automatically", ref folder, 1024))
                LauncherModel.FolderTyped(draft, folder);

            if (OperatingSystem.IsWindows())
            {
                if (ImGui.Button("Browse...") && NativeFileDialog.PickFile(window.NativeHandle,
                        "Find SWTOR's launcher.exe", "SWTOR launcher (launcher.exe)", "launcher.exe",
                        model.DraftInstallation(draft)?.Folder ?? model.Installation?.Folder) is { } picked)
                    model.FolderPicked(draft, picked);
                ImGui.SameLine();
            }
            if (ImGui.Button("Find automatically")) model.FindAutomatically(draft);

            var found = model.DraftInstallation(draft);
            if (found is not null)
                Muted(draft.FolderIsAutomatic
                    ? found.IsSteam ? "Found automatically in your Steam library." : "Found automatically."
                    : found.IsSteam ? "Your choice (a Steam install)." : "Your choice.");
            else if (!draft.FolderIsAutomatic && !string.IsNullOrWhiteSpace(draft.FolderInput))
                Message(false, "No launcher.exe in this folder.");
            else
                Muted("Ashla looks for SWTOR in your Steam libraries and the standard install folder.");

            ImGui.Dummy(new Vector2(0, 4f));
            ImGui.SeparatorText("When You Press Play");
            if (ImGui.RadioButton("Open the SWTOR launcher", draft.Method == GameLaunchMethod.SwtorLauncher))
                draft.Method = GameLaunchMethod.SwtorLauncher;
            if (ImGui.RadioButton("Start SWTOR through Steam", draft.Method == GameLaunchMethod.Steam))
                draft.Method = GameLaunchMethod.Steam;
            if (ImGui.RadioButton("I'll start SWTOR myself", draft.Method == GameLaunchMethod.Manual))
                draft.Method = GameLaunchMethod.Manual;
            Muted("Ashla only starts the game's own launcher. It never sees your login.");
        }

        private void DrawOverlay(SettingsDraft draft)
        {
            DrawHotkey(draft, inSettings: true);

            ImGui.Dummy(new Vector2(0, 4f));
            ImGui.SeparatorText("Toasts");
            CornerPicker(draft, window.UiScale);

            ImGui.Dummy(new Vector2(0, 4f));
            ImGui.SeparatorText("Panel Positions");
            if (ImGui.Button("Reset panel positions")) model.ResetPanelPositions(draft);
            Muted("Puts every overlay panel back in its starting place, for example if one ended up off screen.");
        }

        private void DrawAppearance(SettingsDraft draft)
        {
            ImGui.SeparatorText("Theme");
            ThemePicker(draft, window.UiScale);

            ImGui.Dummy(new Vector2(0, 4f));
            ImGui.SeparatorText("UI Scale");
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 16f);
            if (ImGui.BeginCombo("##scale", ScaleLabel(draft.UiScale)))
            {
                if (ImGui.Selectable(ScaleLabel(null), draft.UiScale is null)) draft.UiScale = null;
                foreach (var choice in OverlaySettings.Choices)
                    if (ImGui.Selectable(ScaleLabel(choice), draft.UiScale == choice)) draft.UiScale = choice;
                ImGui.EndCombo();
            }
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextMuted);
            ImGui.TextWrapped("Size of Ashla's windows and text. Automatic follows your Windows display scaling. " +
                              "The overlay uses it when it opens; the launcher the next time Ashla starts.");
            ImGui.PopStyleColor();
        }

        private static void DrawHotkey(SettingsDraft draft, bool inSettings)
        {
            if (inSettings) ImGui.SeparatorText("Show/Hide Hotkey");
            float boxWidth = ImGui.GetFontSize() * 12f;
            if (draft.CapturingHotkey)
            {
                CaptureHotkey(draft);
                ImGui.PushStyleColor(ImGuiCol.Button, AshlaColors.AccentDim);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AshlaColors.AccentDim);
                if (ImGui.Button("Press the keys...##hotkey", new Vector2(boxWidth, 0)))
                    LauncherModel.CancelHotkeyCapture(draft);
                ImGui.PopStyleColor(2);
                Muted("Hold Ctrl or Alt (Shift too, if you like) and press a key. Esc cancels, Backspace sets no hotkey.");
                return;
            }

            if (ImGui.Button((draft.ToggleHotkey?.ToString() ?? "None") + "##hotkey", new Vector2(boxWidth, 0)))
                LauncherModel.StartHotkeyCapture(draft);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click, then press the new combination");
            ImGui.SameLine();
            if (ImGui.Button("Change")) LauncherModel.StartHotkeyCapture(draft);
            if (draft.ToggleHotkey != Hotkey.Default)
            {
                ImGui.SameLine();
                if (ImGui.Button("Default")) draft.ToggleHotkey = Hotkey.Default;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Go back to {Hotkey.DefaultText}");
            }
            if (draft.ToggleHotkey is null || inSettings) Muted(draft.ToggleHotkey is null
                ? "No hotkey. You can still hide and show Ashla from its icon in the taskbar's notification area."
                : "Hides or shows the overlay from anywhere, including while you play. Ashla's icon in the " +
                  "taskbar's notification area does the same.");
        }

        private static void CaptureHotkey(SettingsDraft draft)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                LauncherModel.CancelHotkeyCapture(draft);
                ImGui.SetNavCursorVisible(false);
                return;
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Backspace, false))
            {
                LauncherModel.ClearHotkey(draft);
                ImGui.SetNavCursorVisible(false);
                return;
            }

            var io = ImGui.GetIO();
            var modifiers = HotkeyModifiers.None;
            if (io.KeyCtrl) modifiers |= HotkeyModifiers.Ctrl;
            if (io.KeyAlt) modifiers |= HotkeyModifiers.Alt;
            if (io.KeyShift) modifiers |= HotkeyModifiers.Shift;
            if (io.KeySuper) modifiers |= HotkeyModifiers.Win;
            foreach (var key in HotkeyKey.All)
            {
                if (!ImGui.IsKeyPressed(key.ImGuiKey, false)) continue;
                LauncherModel.HotkeyPressed(draft, new Hotkey(modifiers, key));
                return;
            }
        }

        private static string ThemeLabel(ThemeChoice choice) => choice == ThemeChoice.Auto
            ? "Auto (follows your character)"
            : ThemeSelection.Resolve(choice, null).Name;

        private void ThemePicker(SettingsDraft draft, float scale)
        {
            var choices = Enum.GetValues<ThemeChoice>();
            float gap = 8f * scale;
            float cardWidth = MathF.Min(100f * scale, (ImGui.GetContentRegionAvail().X - gap * (choices.Length - 1)) / choices.Length);
            float swatchHeight = MathF.Round(cardWidth * 0.62f);
            float cardHeight = swatchHeight + ImGui.GetTextLineHeight() + 8f * scale;
            var draw = ImGui.GetWindowDrawList();
            for (int i = 0; i < choices.Length; i++)
            {
                var choice = choices[i];
                if (i > 0) ImGui.SameLine(0, gap);
                var origin = ImGui.GetCursorScreenPos();
                if (ImGui.InvisibleButton($"##theme.{choice}", new Vector2(cardWidth, cardHeight))) model.ChooseTheme(draft, choice);
                bool hovered = ImGui.IsItemHovered(), chosen = draft.Theme == choice;
                var size = new Vector2(cardWidth, swatchHeight);
                if (choice == ThemeChoice.Auto)
                {
                    draw.PushClipRect(origin, origin + new Vector2(cardWidth * 0.5f, swatchHeight), true);
                    Swatch(draw, AshlaPalette.Republic, origin, size, scale);
                    draw.PopClipRect();
                    draw.PushClipRect(origin + new Vector2(cardWidth * 0.5f, 0), origin + size, true);
                    Swatch(draw, AshlaPalette.Empire, origin, size, scale);
                    draw.PopClipRect();
                }
                else
                {
                    Swatch(draw, model.ThemeFor(choice), origin, size, scale);
                }
                float outline = chosen ? 2f * scale : 1f;
                var outlineColor = chosen ? AshlaColors.Accent : hovered ? AshlaColors.TextMuted : AshlaColors.Border;
                draw.AddRect(origin - new Vector2(outline * 0.5f), origin + size + new Vector2(outline * 0.5f),
                    outlineColor.U32(), AshlaTheme.Round(5f * scale), ImDrawFlags.None, outline);
                string name = choice == ThemeChoice.Auto ? "Auto" : model.ThemeFor(choice).Name;
                var nameSize = ImGui.CalcTextSize(name);
                draw.AddText(origin + new Vector2((cardWidth - nameSize.X) * 0.5f, swatchHeight + 5f * scale),
                    (chosen ? AshlaColors.Text : AshlaColors.TextMuted).U32(), name);
            }

            Muted(draft.Theme switch
            {
                ThemeChoice.Auto => "Republic or Empire, from your character's class in the combat log. " +
                                    (model.LastFaction is { } faction
                                        ? $"Your last character was {(faction == Faction.Republic ? "Republic" : "Imperial")}."
                                        : "Until Ashla sees a character, it uses the Ashla theme."),
                ThemeChoice.Republic => "Deep navy with Republic gold.",
                ThemeChoice.Empire => "Black, crimson and steel, with sharp corners.",
                ThemeChoice.Mandalorian => "Gunmetal and beskar with a red visor accent.",
                _ => "Ashla's own slate and blue."
            });
        }

        private static void Swatch(ImDrawListPtr draw, AshlaPalette palette, Vector2 min, Vector2 size, float scale)
        {
            float rounding = 5f * scale * palette.Rounding;
            var max = min + size;
            draw.AddRectFilled(min, max, palette.Background.U32(), rounding);
            float pad = size.X * 0.09f;
            draw.AddRectFilled(min, new Vector2(max.X, min.Y + size.Y * 0.22f), palette.Surface.U32(), rounding,
                ImDrawFlags.RoundCornersTop);
            draw.AddCircleFilled(new Vector2(min.X + pad, min.Y + size.Y * 0.11f), size.Y * 0.045f, palette.Accent.U32());
            float barHeight = size.Y * 0.13f, barRounding = MathF.Min(barHeight * 0.4f, 3f * scale) * palette.Rounding;
            var bar1 = new Vector2(min.X + pad, min.Y + size.Y * 0.36f);
            draw.AddRectFilled(bar1, bar1 + new Vector2((size.X - pad * 2) * 0.82f, barHeight), palette.Accent.U32(), barRounding);
            var bar2 = new Vector2(min.X + pad, min.Y + size.Y * 0.56f);
            draw.AddRectFilled(bar2, bar2 + new Vector2((size.X - pad * 2) * 0.55f, barHeight), palette.Companion.U32(), barRounding);
            var button = new Vector2(max.X - pad - size.X * 0.3f, max.Y - pad * 0.8f - barHeight);
            draw.AddRectFilled(button, button + new Vector2(size.X * 0.3f, barHeight), palette.AccentDim.U32(), barRounding);
        }

        private static string CornerName(ToastCorner corner) => corner switch
        {
            ToastCorner.TopLeft => "Top left",
            ToastCorner.TopRight => "Top right",
            ToastCorner.BottomLeft => "Bottom left",
            _ => "Bottom right"
        };

        private static void CornerPicker(SettingsDraft draft, float scale)
        {
            var screen = new Vector2(176f, 99f) * scale;
            var toast = new Vector2(52f, 16f) * scale;
            float inset = 7f * scale;
            var origin = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(origin, origin + screen, AshlaColors.Background.U32(), 4f * scale);
            draw.AddRect(origin, origin + screen, AshlaColors.Border.U32(), 4f * scale);

            foreach (var corner in Enum.GetValues<ToastCorner>())
            {
                bool right = corner is ToastCorner.TopRight or ToastCorner.BottomRight;
                bool bottom = corner is ToastCorner.BottomLeft or ToastCorner.BottomRight;
                var min = origin + new Vector2(right ? screen.X - inset - toast.X : inset,
                    bottom ? screen.Y - inset - toast.Y : inset);
                ImGui.SetCursorScreenPos(min);
                if (ImGui.InvisibleButton($"##corner.{corner}", toast)) draft.ToastCorner = corner;
                bool hovered = ImGui.IsItemHovered(), chosen = draft.ToastCorner == corner;
                if (hovered) ImGui.SetTooltip(CornerName(corner));
                var fill = chosen ? AshlaColors.AccentDim : hovered ? AshlaColors.SurfaceRaised : AshlaColors.Surface;
                draw.AddRectFilled(min, min + toast, fill.U32(), 3f * scale);
                draw.AddRect(min, min + toast, (chosen ? AshlaColors.Accent : AshlaColors.Border).U32(), 3f * scale);
                if (chosen)
                    draw.AddRectFilled(min, min + new Vector2(3f * scale, toast.Y), AshlaColors.Accent.U32(), 3f * scale,
                        ImDrawFlags.RoundCornersLeft);
            }

            ImGui.SetCursorScreenPos(origin + new Vector2(screen.X + 14f * scale, 0f));
            ImGui.BeginGroup();
            ImGui.TextColored(AshlaColors.Text, CornerName(draft.ToastCorner));
            ImGui.PushTextWrapPos(0f);
            Muted("Click a corner. Hovering a toast keeps it on screen; clicking it closes it.");
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            ImGui.SetCursorScreenPos(origin + new Vector2(0f, screen.Y + ImGui.GetStyle().ItemSpacing.Y));
            ImGui.Dummy(Vector2.Zero);
        }

        private static string ScaleLabel(float? scale) => scale is { } value
            ? (value * 100f).ToString("0", CultureInfo.InvariantCulture) + "%"
            : "Automatic (Windows scaling)";

        private string _search = "";
        private string _repositoryInput = "";
        private string? _confirmRepository;
        private string? _confirmRemove;
        private string? _confirmUnreviewed;

        private void DrawPlugins(SettingsDraft draft)
        {
            if (!ImGui.BeginTabBar("plugin-tabs")) return;
            if (ImGui.BeginTabItem("Installed"))
            {
                DrawInstalled(draft);
                ImGui.EndTabItem();
            }
            if (model.StoreAvailable && ImGui.BeginTabItem("Browse"))
            {
                if (!model.CatalogLoaded) model.RefreshCatalog();
                DrawBrowse(draft);
                ImGui.EndTabItem();
            }
            if (model.StoreAvailable && ImGui.BeginTabItem("Repositories"))
            {
                DrawRepositories(draft);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        private void DrawInstalled(SettingsDraft draft)
        {
            if (draft.Plugins.Count == 0) Muted("No plugins installed. Find some in Browse.");
            foreach (var plugin in draft.Plugins)
            {
                ImGui.PushID(plugin.Id);
                bool enabled = draft.PluginEnabled.GetValueOrDefault(plugin.Id, true);
                if (ImGui.Checkbox(plugin.DisplayName, ref enabled)) draft.PluginEnabled[plugin.Id] = enabled;
                ImGui.SameLine();
                Muted(plugin.Author is null ? plugin.Version : $"{plugin.Version}  ·  {plugin.Author}");
                var update = model.Updates.FirstOrDefault(u => string.Equals(u.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
                DrawInstalledButtons(draft, plugin, update);

                float indent = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
                ImGui.Indent(indent);
                ImGui.PushTextWrapPos(0f);
                if (plugin.Description is { } description) Muted(description);
                Muted(SourceLabel(model.SourceOf(plugin.Id)));
                if (update is not null && update.Entry.Plugin.Changes is { } changes) Muted($"New in {update.Entry.Plugin.Version}: {changes}");
                ImGui.PopTextWrapPos();
                if (plugin.Problem is { } problem) Message(false, $"Won't load: {problem}");
                if (_confirmRemove == plugin.Id)
                {
                    Message(false, $"Remove {plugin.DisplayName}? Its files are deleted; its settings stay.");
                    if (ImGui.Button("Remove it"))
                    {
                        model.Remove(draft, plugin);
                        _confirmRemove = null;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Keep it")) _confirmRemove = null;
                }
                ImGui.Unindent(indent);
                ImGui.Dummy(new Vector2(0, 2f));
                ImGui.PopID();
            }

            ImGui.Separator();
            Muted("Turned-off plugins don't load. Changes apply when you press Play.");
            if (ImGui.Button("Open plugins folder")) OpenFolder(folders.Plugins);
            if (!model.StoreAvailable) return;
            ImGui.SameLine();
            ImGui.BeginDisabled(model.Fetching);
            if (ImGui.Button(model.Fetching ? "Checking..." : "Check for updates"))
            {
                model.RefreshCatalog();
                model.CheckAshla();
            }
            ImGui.EndDisabled();
            ImGui.Dummy(new Vector2(0, 2f));
            bool check = draft.CheckForUpdates;
            if (ImGui.Checkbox("Check for updates when Ashla starts", ref check)) draft.CheckForUpdates = check;
            Muted("Ashla asks GitHub whether there's a newer Ashla, and downloads the plugin lists from Holocron (and your " +
                  "other repositories). Nothing about you or your game is sent.");
        }

        private void DrawInstalledButtons(SettingsDraft draft, InstalledPlugin plugin, PluginUpdate? update)
        {
            var style = ImGui.GetStyle();
            string updateLabel = update is null ? "" : $"Update to {update.Entry.Plugin.Version}";
            bool busy = model.IsInstalling(plugin.Id);
            float width = ImGui.CalcTextSize("Remove").X + style.FramePadding.X * 2;
            if (busy) width += ImGui.CalcTextSize("Installing...").X + style.FramePadding.X * 2 + style.ItemSpacing.X;
            else if (update is not null) width += ImGui.CalcTextSize(updateLabel).X + style.FramePadding.X * 2 + style.ItemSpacing.X;
            SameLineRight(width);
            if (busy)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Installing...");
                ImGui.EndDisabled();
                ImGui.SameLine();
            }
            else if (update is not null)
            {
                if (ActionButton(updateLabel)) model.Install(draft, update.Entry);
                ImGui.SameLine();
            }
            ImGui.BeginDisabled(busy);
            if (ImGui.Button("Remove")) _confirmRemove = plugin.Id;
            ImGui.EndDisabled();
        }

        private void DrawBrowse(SettingsDraft draft)
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
            ImGui.InputTextWithHint("##search", "Search plugins", ref _search, 100);
            ImGui.SameLine();
            ImGui.BeginDisabled(model.Fetching);
            if (ImGui.Button(model.Fetching ? "Loading..." : "Refresh")) model.RefreshCatalog();
            ImGui.EndDisabled();
            foreach (var repository in model.Catalog.Repositories.Where(r => r.Problem is not null))
                Message(false, repository.Problem!);
            if (model.Fetching && !model.CatalogLoaded)
            {
                Muted("Loading the plugin lists...");
                return;
            }

            var query = _search.Trim();
            var entries = model.Catalog.Entries
                .Where(e => query.Length == 0 || Matches(e.Plugin, query))
                .OrderByDescending(e => e.Repository.Official)
                .ThenBy(e => e.Plugin.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (entries.Count == 0)
            {
                Muted(model.Catalog.Entries.Count == 0 ? "No plugins to show yet." : "No plugins match.");
                return;
            }
            ImGui.Spacing();
            foreach (var entry in entries) DrawCatalogEntry(draft, entry);
        }

        private static bool Matches(RepoPlugin plugin, string query) =>
            plugin.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            (plugin.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            (plugin.Author?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            plugin.Tags.Any(t => t.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        private void DrawCatalogEntry(SettingsDraft draft, CatalogEntry entry)
        {
            var plugin = entry.Plugin;
            string key = $"{entry.Repository.Url}|{plugin.Id}";
            ImGui.PushID(key);
            ImGui.TextColored(AshlaColors.Text, plugin.DisplayName);
            ImGui.SameLine();
            Muted(plugin.Author is null ? plugin.Version : $"{plugin.Version}  ·  {plugin.Author}");

            var installed = draft.Plugins.FirstOrDefault(p => string.Equals(p.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
            var (label, enabled) = model.IsInstalling(plugin.Id) ? ("Installing...", false)
                : !entry.Compatible ? (plugin.ApiLevel > AshlaApi.Level ? "Needs a newer Ashla" : "Made for an older Ashla", false)
                : installed is null ? ("Install", true)
                : PluginVersions.IsNewer(plugin.Version, installed.Version) ? ($"Update to {plugin.Version}", true)
                : ("Installed", false);
            var style = ImGui.GetStyle();
            float width = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2;
            SameLineRight(width);
            ImGui.BeginDisabled(!enabled);
            bool pressed = enabled ? ActionButton(label) : ImGui.Button(label);
            ImGui.EndDisabled();
            if (pressed)
            {
                if (entry.Repository.Official) model.Install(draft, entry);
                else _confirmUnreviewed = key;
            }

            ImGui.PushTextWrapPos(0f);
            if (!entry.Repository.Official)
                ImGui.TextColored(AshlaColors.Warning, $"From {entry.Repository.Name}. Not reviewed by Holocron.");
            if (plugin.Description is { } description) Muted(description);
            if (plugin.Tags.Count > 0) Muted(string.Join("  ·  ", plugin.Tags));
            ImGui.PopTextWrapPos();
            if (_confirmUnreviewed == key)
            {
                Message(false, $"{plugin.DisplayName} hasn't been reviewed by Holocron. It will run with the same access to " +
                               "your PC as Ashla. Install it only if you trust where it came from.");
                if (ImGui.Button("Install anyway"))
                {
                    model.Install(draft, entry);
                    _confirmUnreviewed = null;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) _confirmUnreviewed = null;
            }
            ImGui.Separator();
            ImGui.PopID();
        }

        private void DrawRepositories(SettingsDraft draft)
        {
            foreach (var repository in model.Repositories)
            {
                ImGui.PushID(repository.Url);
                ImGui.TextColored(AshlaColors.Text, repository.Official ? "Holocron (official, reviewed)" : repository.Name);
                if (!repository.Official)
                {
                    float width = ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2;
                    SameLineRight(width);
                    if (ImGui.Button("Remove")) model.RemoveRepository(draft, repository.Url);
                }
                ImGui.PushTextWrapPos(0f);
                Muted(repository.Url);
                ImGui.PopTextWrapPos();
                ImGui.PopID();
            }

            ImGui.Dummy(new Vector2(0, 4f));
            ImGui.SeparatorText("Add a Repository");
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Add").X - ImGui.GetStyle().FramePadding.X * 2 -
                                   ImGui.GetStyle().ItemSpacing.X);
            ImGui.InputTextWithHint("##repository", "https://.../repo.json", ref _repositoryInput, 300);
            ImGui.SameLine();
            if (ImGui.Button("Add"))
            {
                if (HolocronClient.CheckRepositoryUrl(_repositoryInput) is { } problem)
                {
                    draft.Notice = null;
                    draft.Error = problem;
                }
                else
                {
                    draft.Error = null;
                    _confirmRepository = _repositoryInput.Trim();
                }
            }
            if (_confirmRepository is { } url)
            {
                Message(false, "Plugins from other repositories aren't reviewed by Holocron. A plugin runs with the same access " +
                               "to your PC as Ashla, so only add repositories from people you trust.");
                if (ImGui.Button("Add anyway"))
                {
                    model.AddRepository(draft, url);
                    _confirmRepository = null;
                    _repositoryInput = "";
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) _confirmRepository = null;
            }
            else
            {
                Muted("Paste the address of a repo.json from someone who publishes Ashla plugins.");
            }
        }

        private static string SourceLabel(string? repository) =>
            repository is null ? "Added by hand or came with Ashla."
            : string.Equals(repository, HolocronClient.Official, StringComparison.OrdinalIgnoreCase) ? "From Holocron."
            : $"From {HolocronClient.ShortName(repository)} (not reviewed by Holocron).";

        private static void SameLineRight(float width)
        {
            ImGui.SameLine();
            float available = ImGui.GetContentRegionAvail().X;
            if (available < width)
            {
                ImGui.NewLine();
                available = ImGui.GetContentRegionAvail().X;
            }
            if (available > width) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + available - width);
        }

        private static bool ActionButton(string label)
        {
            var color = AshlaColors.Action;
            ImGui.PushStyleColor(ImGuiCol.Button, color.WithAlpha(0.85f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color.Shade(0.15f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, color.Shade(-0.15f));
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextOn(color));
            bool pressed = ImGui.Button(label);
            ImGui.PopStyleColor(4);
            return pressed;
        }

        private void DrawAbout()
        {
            AshlaFonts.LargeText("Ashla", AshlaColors.Text);
            Muted($"Version {version}");
            if (model.CanCheckAshla)
            {
                if (model.AshlaUpdate is { } release)
                {
                    ImGui.TextColored(AshlaColors.Accent, $"Ashla {release.Version} is available.");
                    if (model.UpdatingAshla) Muted(model.UpdateStatus ?? "Updating...");
                    if (model.UpdateError is { } error) ImGui.TextColored(AshlaColors.Error, error);
                    ReleaseButtons(release, Vector2.Zero, stacked: false);
                }
                else if (model.CheckingAshla) Muted("Checking for a new version...");
                else if (model.AshlaChecked && model.AshlaLatest is not null) Muted("This is the newest version.");
                else if (model.AshlaChecked) Muted("Couldn't reach GitHub to check for a new version.");
                if (!model.CheckingAshla)
                {
                    if (model.AshlaUpdate is not null) ImGui.SameLine();
                    if (ImGui.SmallButton("Check for a new version")) model.CheckAshla();
                }
            }
            ImGui.Spacing();
            ImGui.TextWrapped("Overlays and combat-log tools for Star Wars: The Old Republic. Ashla runs beside the game " +
                              "and only reads the combat logs SWTOR writes; it never touches the game itself.");
            ImGui.Spacing();
            ImGui.TextWrapped("Free software under the GNU Affero General Public License v3.0.");
            if (ImGui.Button("Source code on GitHub")) ShellOpen(SourceUrl);
            var license = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
            var notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
            if (File.Exists(license))
            {
                ImGui.SameLine();
                if (ImGui.Button("License")) ShellOpen(license);
            }
            if (File.Exists(notices))
            {
                ImGui.SameLine();
                if (ImGui.Button("Included libraries")) ShellOpen(notices);
            }
            if (folders.Logs is { } logs)
            {
                ImGui.SameLine();
                if (ImGui.Button("Logs folder")) OpenFolder(logs);
            }
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, AshlaColors.TextMuted);
            ImGui.TextWrapped("Ashla is an independent fan project, not affiliated with or endorsed by Broadsword, " +
                              "Electronic Arts or Lucasfilm.");
            ImGui.PopStyleColor();

            ImGui.Dummy(new Vector2(0, 4f));
            ImGui.SeparatorText("Setup");
            if (ImGui.Button("Run setup again")) model.StartSetup(rerun: true);
            Muted("Goes through the SWTOR folder, combat logging and the hotkey again. Cancel leaves everything as it is.");
        }

        private void DrawFights(SettingsDraft draft)
        {
            ImGui.SeparatorText("Saved Fights");
            bool save = draft.SaveFights;
            if (ImGui.Checkbox("Save fights as they end", ref save)) draft.SaveFights = save;
            Muted("Each fight is kept on this PC, so you can open it again in the Combat Meter, also after a restart.");
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Keep them for");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 9f);
            if (ImGui.BeginCombo("##keep", KeepLabel(draft.KeepDays)))
            {
                foreach (var days in FightSettings.Choices)
                    if (ImGui.Selectable(KeepLabel(days), draft.KeepDays == days)) draft.KeepDays = days;
                ImGui.EndCombo();
            }
            if (model.Archive is not { } archive) return;
            int count = archive.Count;
            Muted(count == 0
                ? "No saved fights yet."
                : $"{ImportResult.Plural(count, "saved fight")}, {ImportResult.Size(archive.SizeOnDisk())}.");

            ImGui.Dummy(new Vector2(0, 4f));
            ImGui.SeparatorText("Older Logs");
            if (OperatingSystem.IsWindows())
            {
                if (ImGui.Button("Open combat log...") && NativeFileDialog.PickFile(window.NativeHandle,
                        "Open a SWTOR combat log", "Combat logs (combat_*.txt)", "combat_*.txt;*.txt",
                        folders.CombatLogs) is { } picked)
                    model.ImportLog(draft, picked);
                ImGui.SameLine();
            }
            if (ImGui.Button("Open saved fights folder")) OpenFolder(archive.Directory);
            Muted("Open combat log adds the fights from a log SWTOR wrote earlier, for example from before you used Ashla.");

            ImGui.Dummy(new Vector2(0, 4f));
            if (count == 0) return;
            if (!_confirmDeleteFights)
            {
                if (ImGui.Button("Delete all saved fights")) _confirmDeleteFights = true;
                return;
            }
            Message(false, $"Delete {ImportResult.Plural(count, "saved fight")}? This can't be undone.");
            if (ImGui.Button("Delete"))
            {
                model.DeleteAllFights(draft);
                _confirmDeleteFights = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Keep them")) _confirmDeleteFights = false;
        }

        private static string KeepLabel(int days) => days <= 0 ? "Forever" : $"{days} days";

        private static void OpenFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            ShellOpen(folder);
        }
    }
}
