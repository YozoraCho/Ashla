using Ashla.CombatLog;
using Ashla.SDK;

namespace Ashla.Tests;

[Collection("AshlaTheme")]
public class ThemeTests
{
    private readonly TestLogger _log = new();

    [Theory]
    [InlineData("Sentinel", "Republic")]
    [InlineData("Scoundrel", "Republic")]
    [InlineData("Jedi Consular", "Republic")]
    [InlineData("Marauder", "Empire")]
    [InlineData("operative", "Empire")]
    [InlineData("Sith Inquisitor", "Empire")]
    public void Factions_FromClassName(string name, string faction) =>
        Assert.Equal(Enum.Parse<Ashla.Host.Faction>(faction), Ashla.Host.Factions.FromClass(new GameRef(name, 1)));

    [Fact]
    public void Factions_FromClassId_AndUnknown()
    {
        Assert.Equal(Ashla.Host.Faction.Republic, Ashla.Host.Factions.FromClass(new GameRef("Wächter", 16141154905109553504)));
        Assert.Null(Ashla.Host.Factions.FromClass(new GameRef("Unbekannt", 2)));
    }

    [Fact]
    public void FactionWatcher_ReportsTheLogOwnersFaction_Once()
    {
        var watcher = new Ashla.Host.FactionWatcher();
        var seen = new List<Ashla.Host.Faction>();
        watcher.FactionChanged += seen.Add;

        foreach (var e in CombatLogFixtureTests.ParseFixture(out _)) watcher.Handle(e);

        Assert.Equal([Ashla.Host.Faction.Republic], seen);
        Assert.Equal(Ashla.Host.Faction.Republic, watcher.Current);
    }

    [Fact]
    public void FactionWatcher_SwitchesWithANewCharacter()
    {
        var first = CombatLogFixtureTests.ParseFixture(out _).First(e => e.Effect.Type.Id == CombatIds.DisciplineChanged);
        var other = first with
        {
            Source = first.Source! with { Name = "Imperial", Id = 1234 },
            Effect = first.Effect with { Effect = new GameRef("Marauder", 99) }
        };
        var watcher = new Ashla.Host.FactionWatcher();
        var seen = new List<Ashla.Host.Faction>();
        watcher.FactionChanged += seen.Add;

        watcher.Handle(first);
        watcher.Handle(other);

        Assert.Equal([Ashla.Host.Faction.Republic, Ashla.Host.Faction.Empire], seen);
    }

    [Theory]
    [InlineData("Auto", null, "Ashla")]
    [InlineData("Auto", "Republic", "Republic")]
    [InlineData("Auto", "Empire", "Empire")]
    [InlineData("Mandalorian", "Empire", "Mandalorian")]
    [InlineData("Ashla", "Republic", "Ashla")]
    public void Resolve_PinnedThemeWins_AutoFollowsFaction(string choice, string? faction, string expected) =>
        Assert.Equal(Enum.Parse<Ashla.UI.AshlaThemeId>(expected), Ashla.Host.ThemeSelection.Resolve(
            Enum.Parse<Ashla.Host.Settings.ThemeChoice>(choice),
            faction is null ? null : Enum.Parse<Ashla.Host.Faction>(faction)).Id);

    [Fact]
    public void Follow_RemembersTheFaction_AndSwitchesOnlyOnAuto()
    {
        using var temp = new TempDirectory();
        var settings = Ashla.Host.Settings.AshlaSettingsStore.Load(temp.Path, _log);
        var watcher = new Ashla.Host.FactionWatcher();
        Ashla.Host.ThemeSelection.Follow(watcher, settings, _log);
        Ashla.UI.AshlaTheme.Use(Ashla.UI.AshlaPalette.Ashla);

        foreach (var e in CombatLogFixtureTests.ParseFixture(out _).Take(5)) watcher.Handle(e);

        Assert.Equal(Ashla.UI.AshlaThemeId.Republic, Ashla.UI.AshlaTheme.Current.Id);
        Assert.Equal(Ashla.Host.Faction.Republic,
            Ashla.Host.Settings.AshlaSettingsStore.Load(temp.Path, _log).Current.Appearance.LastFaction);

        settings.Current.Appearance.Theme = Ashla.Host.Settings.ThemeChoice.Mandalorian;
        Ashla.UI.AshlaTheme.Use(Ashla.UI.AshlaPalette.Mandalorian);
        var marauder = CombatLogFixtureTests.ParseFixture(out _).First(e => e.Effect.Type.Id == CombatIds.DisciplineChanged);
        watcher.Handle(marauder with { Effect = marauder.Effect with { Effect = new GameRef("Marauder", 99) } });
        Assert.Equal(Ashla.UI.AshlaThemeId.Mandalorian, Ashla.UI.AshlaTheme.Current.Id);
        Assert.Equal(Ashla.Host.Faction.Empire, settings.Current.Appearance.LastFaction);
        Ashla.UI.AshlaTheme.Use(Ashla.UI.AshlaPalette.Ashla);
    }

    [Fact]
    public void Palettes_AreComplete_AndColorsFollowTheTheme()
    {
        Assert.Equal(4, Ashla.UI.AshlaPalette.All.Count);
        Assert.Equal(Enum.GetValues<Ashla.UI.AshlaThemeId>(), Ashla.UI.AshlaPalette.All.Select(p => p.Id));
        try
        {
            Ashla.UI.AshlaTheme.Use(Ashla.UI.AshlaPalette.Empire);
            Assert.Equal(Ashla.UI.AshlaPalette.Empire.Accent, Ashla.UI.AshlaColors.Accent);
            Assert.Equal(Ashla.UI.AshlaPalette.Empire.Rounding * 4f, Ashla.UI.AshlaTheme.Round(4f));
            Assert.True(Ashla.UI.AshlaTheme.StyleIsStale);
        }
        finally
        {
            Ashla.UI.AshlaTheme.Use(Ashla.UI.AshlaPalette.Ashla);
        }
    }

    [Fact]
    public void ThemeSetting_DefaultsToAuto()
    {
        var appearance = new Ashla.Host.Settings.AppearanceSettings();
        Assert.Equal(Ashla.Host.Settings.ThemeChoice.Auto, appearance.Theme);
        Assert.Null(appearance.LastFaction);
    }
}
