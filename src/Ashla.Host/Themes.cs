using Ashla.Host.Settings;
using Ashla.SDK;
using Ashla.UI;

namespace Ashla.Host;

internal enum Faction
{
    Republic,
    Empire
}

internal static class Factions
{
    private static readonly Dictionary<string, Faction> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jedi Knight"] = Faction.Republic, ["Guardian"] = Faction.Republic, ["Sentinel"] = Faction.Republic,
        ["Jedi Consular"] = Faction.Republic, ["Sage"] = Faction.Republic, ["Shadow"] = Faction.Republic,
        ["Trooper"] = Faction.Republic, ["Commando"] = Faction.Republic, ["Vanguard"] = Faction.Republic,
        ["Smuggler"] = Faction.Republic, ["Gunslinger"] = Faction.Republic, ["Scoundrel"] = Faction.Republic,
        ["Sith Warrior"] = Faction.Empire, ["Juggernaut"] = Faction.Empire, ["Marauder"] = Faction.Empire,
        ["Sith Inquisitor"] = Faction.Empire, ["Sorcerer"] = Faction.Empire, ["Assassin"] = Faction.Empire,
        ["Bounty Hunter"] = Faction.Empire, ["Mercenary"] = Faction.Empire, ["Powertech"] = Faction.Empire,
        ["Imperial Agent"] = Faction.Empire, ["Sniper"] = Faction.Empire, ["Operative"] = Faction.Empire,
    };

    private static readonly Dictionary<ulong, Faction> ById = new()
    {
        [16141154905109553504] = Faction.Republic,
    };

    public static Faction? FromClass(GameRef characterClass) =>
        ById.TryGetValue(characterClass.Id, out var byId) ? byId
        : ByName.TryGetValue(characterClass.Name.Trim(), out var byName) ? byName
        : null;
}

internal sealed class FactionWatcher
{
    private readonly object _gate = new();
    private ulong? _ownerId;
    private Faction? _faction;

    public event Action<Faction>? FactionChanged;

    public Faction? Current
    {
        get { lock (_gate) return _faction; }
    }

    public void Handle(CombatEvent e)
    {
        Faction? changed = null;
        lock (_gate)
        {
            if (e.Effect.Type.Id is CombatIds.AreaEntered or CombatIds.DisciplineChanged && e.Source is { IsPlayer: true } owner)
                _ownerId = owner.Id;
            if (e.Effect.Type.Id != CombatIds.DisciplineChanged || e.Source is not { IsPlayer: true } source ||
                source.Id != _ownerId)
                return;
            if (Factions.FromClass(e.Effect.Effect) is not { } faction || faction == _faction) return;
            _faction = faction;
            changed = faction;
        }
        if (changed is { } now) FactionChanged?.Invoke(now);
    }
}

internal static class ThemeSelection
{
    public static AshlaPalette Resolve(ThemeChoice choice, Faction? faction) => choice switch
    {
        ThemeChoice.Ashla => AshlaPalette.Ashla,
        ThemeChoice.Republic => AshlaPalette.Republic,
        ThemeChoice.Empire => AshlaPalette.Empire,
        ThemeChoice.Mandalorian => AshlaPalette.Mandalorian,
        _ => faction switch
        {
            Faction.Republic => AshlaPalette.Republic,
            Faction.Empire => AshlaPalette.Empire,
            _ => AshlaPalette.Ashla
        }
    };

    public static void Apply(AppearanceSettings appearance) =>
        AshlaTheme.Use(Resolve(appearance.Theme, appearance.LastFaction));

    public static void Follow(FactionWatcher watcher, AshlaSettingsStore settings, ILogger log)
    {
        watcher.FactionChanged += faction =>
        {
            var appearance = settings.Current.Appearance;
            if (appearance.LastFaction != faction)
            {
                appearance.LastFaction = faction;
                settings.Save();
            }
            if (appearance.Theme != ThemeChoice.Auto) return;
            Apply(appearance);
            log.Info($"Theme follows your character: {AshlaTheme.Current.Name}.");
        };
    }
}
