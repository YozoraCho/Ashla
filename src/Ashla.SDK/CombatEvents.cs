namespace Ashla.SDK;

public sealed record GameRef(string Name, ulong Id)
{
    public override string ToString() => $"{Name} {{{Id}}}";
}

public enum CombatEntityKind
{
    Player,
    Companion,
    Npc,

    Unknown
}

public readonly record struct CombatPosition(double X, double Y, double Z, double Facing);

public readonly record struct CombatHealth(long Current, long Max);

public sealed record CombatEntity(
    CombatEntityKind Kind,
    string Name,
    ulong Id,
    ulong? InstanceId,
    CombatEntity? Owner,
    CombatPosition? Position,
    CombatHealth? Health)
{
    public bool IsPlayer => Kind == CombatEntityKind.Player;

    public bool IsOrBelongsTo(ulong playerId) =>
        (Kind == CombatEntityKind.Player && Id == playerId) || Owner?.Id == playerId;

    public override string ToString() => Owner is null ? Name : $"{Owner.Name}/{Name}";
}

public sealed record CombatEffect(GameRef Type, GameRef Effect, GameRef? Detail = null);

public sealed record CombatValue(
    double? Amount,
    bool IsCritical,
    double? Effective,
    GameRef? Kind,
    GameRef? Mitigation,
    bool HasUnnamedMitigation,
    double? Absorbed,
    string Raw,
    bool IsFullyParsed)
{
    public double? EffectiveOrAmount => Effective ?? Amount;
}

public sealed record CombatEvent(
    DateTime Timestamp,
    CombatEntity? Source,
    CombatEntity? Target,
    GameRef? Ability,
    CombatEffect Effect,
    CombatValue? Value,
    double? Threat,
    string? Annotation,
    string SourceFile,
    string RawText)
{
    public bool IsDamage => Effect.Type.Id == CombatIds.ApplyEffect && Effect.Effect.Id == CombatIds.Damage;
    public bool IsHeal => Effect.Type.Id == CombatIds.ApplyEffect && Effect.Effect.Id == CombatIds.Heal;
    public bool IsEvent(ulong eventId) => Effect.Type.Id == CombatIds.Event && Effect.Effect.Id == eventId;
}

public static class CombatIds
{
    public const ulong Event = 836045448945472;
    public const ulong Spend = 836045448945473;
    public const ulong Restore = 836045448945476;
    public const ulong ApplyEffect = 836045448945477;
    public const ulong RemoveEffect = 836045448945478;
    public const ulong AreaEntered = 836045448953664;
    public const ulong DisciplineChanged = 836045448953665;
    public const ulong ModifyCharges = 836045448953666;

    public const ulong Heal = 836045448945500;
    public const ulong Damage = 836045448945501;

    public const ulong AbilityActivate = 836045448945479;
    public const ulong AbilityDeactivate = 836045448945480;
    public const ulong AbilityCancel = 836045448945481;
    public const ulong AbilityInterrupt = 836045448945482;
    public const ulong ModifyThreat = 836045448945483;
    public const ulong EnterCombat = 836045448945489;
    public const ulong ExitCombat = 836045448945490;
    public const ulong Death = 836045448945493;
    public const ulong FailedEffect = 836045448945499;
    public const ulong TargetSet = 836045448953668;
    public const ulong TargetCleared = 836045448953669;

    public const ulong KineticDamage = 836045448940873;
    public const ulong EnergyDamage = 836045448940874;
    public const ulong ElementalDamage = 836045448940875;

    public const ulong Miss = 836045448945502;
    public const ulong Parry = 836045448945503;
    public const ulong Deflect = 836045448945508;
    public const ulong Shield = 836045448945509;
    public const ulong Absorbed = 836045448945511;
}
