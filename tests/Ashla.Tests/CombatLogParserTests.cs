using Ashla.CombatLog;
using Ashla.Host;
using Ashla.SDK;

namespace Ashla.Tests;

public class CombatLogParserTests
{
    private static readonly DateTime Day = new(2026, 10, 22);
    private const ulong PlayerId = 690000000000001;

    private static CombatEvent Parse(string line)
    {
        Assert.True(CombatLogParser.TryParse(line, Day, "test.txt", out var parsed), $"Didn't parse: {line}");
        return parsed!;
    }

    [Fact]
    public void CriticalDamage_WithShieldAbsorb()
    {
        var e = Parse("[04:10:22.091] [@Tëstplayer#690000000000001|(-251.34,-176.31,-28.29,127.45)|(105443/105443)] [Scour Swarm Droid {4080626953093120}:22228027650322|(-252.85,-175.16,-28.29,-62.37)|(15088/29105)] [Strike {947362411315200}] [ApplyEffect {836045448945477}: Damage {836045448945501}] (321* energy {836045448940874} -shield {836045448945509} (167 absorbed {836045448945511})) <321.0>");

        Assert.Equal(Day + new TimeSpan(0, 4, 10, 22, 91), e.Timestamp);
        Assert.True(e.IsDamage);
        Assert.Equal(new GameRef("Strike", 947362411315200), e.Ability);

        var source = e.Source!;
        Assert.Equal(CombatEntityKind.Player, source.Kind);
        Assert.Equal("Tëstplayer", source.Name);
        Assert.Equal(PlayerId, source.Id);
        Assert.Equal(new CombatPosition(-251.34, -176.31, -28.29, 127.45), source.Position);
        Assert.Equal(new CombatHealth(105443, 105443), source.Health);

        var target = e.Target!;
        Assert.Equal(CombatEntityKind.Npc, target.Kind);
        Assert.Equal("Scour Swarm Droid", target.Name);
        Assert.Equal(4080626953093120UL, target.Id);
        Assert.Equal(22228027650322UL, target.InstanceId);
        Assert.Equal(new CombatHealth(15088, 29105), target.Health);

        var v = e.Value!;
        Assert.True(v.IsFullyParsed);
        Assert.Equal(321, v.Amount);
        Assert.True(v.IsCritical);
        Assert.Null(v.Effective);
        Assert.Equal(321, v.EffectiveOrAmount);
        Assert.Equal(CombatIds.EnergyDamage, v.Kind!.Id);
        Assert.Equal("energy", v.Kind.Name);
        Assert.Equal(new GameRef("shield", CombatIds.Shield), v.Mitigation);
        Assert.Equal(167, v.Absorbed);
        Assert.Equal(321.0, e.Threat);
    }

    [Fact]
    public void Miss_HasZeroAmountAndMitigation()
    {
        var e = Parse("[04:07:17.963] [Separatist Siege Gunner {830320257531904}:22299054771863|(126.84,-195.82,43.61,-94.77)|(100/100)] [@Tëstplayer#690000000000001|(143.28,-194.23,42.72,146.39)|(2722/2747)] [Ranged Attack {813449625993216}] [ApplyEffect {836045448945477}: Damage {836045448945501}] (0 -miss {836045448945502}) <1.0>");
        Assert.Equal(0, e.Value!.Amount);
        Assert.Null(e.Value.Kind);
        Assert.Equal(CombatIds.Miss, e.Value.Mitigation!.Id);
        Assert.True(e.Target!.IsPlayer);
    }

    [Fact]
    public void CompanionHeal_WithEffectiveAmount()
    {
        var e = Parse("[04:08:25.866] [@Tëstplayer#690000000000001/Kira Carsen {349412769398784}:22228027764826|(227.10,976.66,526.15,72.60)|(121191/121191)] [@Tëstplayer#690000000000001|(223.03,975.99,526.02,83.47)|(105443/105443)] [Mending {3569512959967232}] [ApplyEffect {836045448945477}: Heal {836045448945500}] (3630 ~291) <58.0>");

        Assert.True(e.IsHeal);
        var companion = e.Source!;
        Assert.Equal(CombatEntityKind.Companion, companion.Kind);
        Assert.Equal("Kira Carsen", companion.Name);
        Assert.Equal(349412769398784UL, companion.Id);
        Assert.Equal(22228027764826UL, companion.InstanceId);
        Assert.Equal("Tëstplayer", companion.Owner!.Name);
        Assert.Equal(PlayerId, companion.Owner.Id);
        Assert.True(companion.IsOrBelongsTo(PlayerId));
        Assert.Equal(new CombatHealth(121191, 121191), companion.Health);

        Assert.Equal(3630, e.Value!.Amount);
        Assert.Equal(291, e.Value.Effective);
        Assert.Equal(291, e.Value.EffectiveOrAmount);
        Assert.Equal(58.0, e.Threat);
    }

    [Fact]
    public void SameAsSourceTarget_AndEmptyAbility()
    {
        var e = Parse("[04:07:16.378] [@Tëstplayer#690000000000001|(148.67,-187.44,43.23,-4.30)|(2747/2747)] [=] [] [Restore {836045448945476}: focus point {836045448938496}] (1.0)");
        Assert.Same(e.Source, e.Target);
        Assert.Null(e.Ability);
        Assert.Equal(CombatIds.Restore, e.Effect.Type.Id);
        Assert.Equal("focus point", e.Effect.Effect.Name);
        Assert.Equal(1.0, e.Value!.Amount);
        Assert.Null(e.Threat);
    }

    [Fact]
    public void AreaEntered_KeepsUnknownValueRaw_AndVersionAnnotation()
    {
        var e = Parse("[04:06:44.499] [@Tëstplayer#690000000000001|(166.14,-32.68,61.12,84.36)|(2630/2630)] [] [] [AreaEntered {836045448953664}: Ord Mantell {137438954894}] (he3000) <v7.0.0b>");
        Assert.Null(e.Target);
        Assert.Equal(CombatIds.AreaEntered, e.Effect.Type.Id);
        Assert.Equal(new GameRef("Ord Mantell", 137438954894), e.Effect.Effect);
        Assert.False(e.Value!.IsFullyParsed);
        Assert.Equal("he3000", e.Value.Raw);
        Assert.Null(e.Value.Amount);
        Assert.Null(e.Threat);
        Assert.Equal("v7.0.0b", e.Annotation);
    }

    [Fact]
    public void DisciplineChanged_HasClassAndDiscipline()
    {
        var e = Parse("[04:06:44.499] [@Tëstplayer#690000000000001|(166.14,-32.68,61.12,84.36)|(2630/2630)] [] [] [DisciplineChanged {836045448953665}: Sentinel {16141154905109553504}/Concentration {2031339142381611}]");
        Assert.Equal(new GameRef("Sentinel", 16141154905109553504), e.Effect.Effect);
        Assert.Equal(new GameRef("Concentration", 2031339142381611), e.Effect.Detail);
        Assert.Null(e.Value);
    }

    [Fact]
    public void Death_TargetIsWhoDied()
    {
        var e = Parse("[04:07:16.890] [@Tëstplayer#690000000000001|(148.61,-191.11,43.16,56.23)|(2747/2747)] [Separatist Siege Gunner {830320257531904}:22299054771818|(147.55,-191.08,43.34,150.00)|(0/110)] [] [Event {836045448945472}: Death {836045448945493}]");
        Assert.True(e.IsEvent(CombatIds.Death));
        Assert.Equal(new CombatHealth(0, 110), e.Target!.Health);
    }

    [Fact]
    public void Overkill_EffectiveZero_WithBareMitigationDash()
    {
        var e = Parse("[04:10:52.646] [@Tëstplayer#690000000000001|(-270.44,-194.77,-28.18,-157.86)|(105443/105443)] [Caretaker Remote {4080558233616384}:22228026618523|(-273.84,-201.23,-28.18,-111.69)|(0/14085)] [Strike {947362411315200}] [ApplyEffect {836045448945477}: Damage {836045448945501}] (749 ~0 energy {836045448940874} -)");
        Assert.True(e.Value!.IsFullyParsed);
        Assert.Equal(749, e.Value.Amount);
        Assert.Equal(0, e.Value.Effective);
        Assert.True(e.Value.HasUnnamedMitigation);
        Assert.Null(e.Value.Mitigation);
    }

    [Fact]
    public void Charges_AreAValueKind()
    {
        var e = Parse("[04:07:17.848] [@Tëstplayer#690000000000001|(144.07,-194.45,42.78,129.54)|(2722/2747)] [=] [Centering {2528580391141376}] [ModifyCharges {836045448953666}: Centering {2528580391141376}] (6 charges {836045448953667})");
        Assert.Equal(6, e.Value!.Amount);
        Assert.Equal(new GameRef("charges", 836045448953667), e.Value.Kind);
    }

    [Fact]
    public void ThreatOnlyEvent_AbilityWithParentheses()
    {
        var threat = Parse("[04:14:59.178] [@Tëstplayer#690000000000001|(4788.42,4702.23,1690.02,-108.61)|(374536/374536)] [Combat Training Target {2816261595594752}:22294000006393|(4790.39,4702.89,1690.02,90.00)|(99982008/100001144)] [Force Kick {997020823191552}] [Event {836045448945472}: ModifyThreat {836045448945483}] <1.0>");
        Assert.Null(threat.Value);
        Assert.Equal(1.0, threat.Threat);

        var burn = Parse("[04:10:29.671] [Scour Swarm Droid {4080626953093120}:22228027650397|(-251.94,-169.83,-28.18,-27.74)|(2874/29105)] [@Tëstplayer#690000000000001|(-250.82,-171.97,-28.29,152.36)|(99028/105443)] [Burning (Tech) {3965577664135442}] [ApplyEffect {836045448945477}: Damage {836045448945501}] (242 elemental {836045448940875}) <242.0>");
        Assert.Equal(new GameRef("Burning (Tech)", 3965577664135442), burn.Ability);
        Assert.Equal(CombatIds.ElementalDamage, burn.Value!.Kind!.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("DEMO: player enters combat")]
    [InlineData("[04:06:44.499] [] [] [] []")]
    [InlineData("[25:06:44.499] [] [] [] [Event {1}: Death {2}]")]
    public void NonLogLines_AreRejected(string line)
    {
        Assert.False(CombatLogParser.TryParse(line, Day, "x", out _));
    }

    [Fact]
    public void UnknownEntityFormat_IsKeptRawInsteadOfGuessed()
    {
        var e = Parse("[04:06:44.499] [Something new] [] [] [Event {836045448945472}: Death {836045448945493}]");
        Assert.Equal(CombatEntityKind.Unknown, e.Source!.Kind);
        Assert.Equal("Something new", e.Source.Name);
    }

    [Fact]
    public void UnrecognizedValue_KeepsLeadingAmountAndRawText()
    {
        var v = CombatLogParser.ParseValue("1234* energy {836045448940874}(reflected {1})");
        Assert.False(v.IsFullyParsed);
        Assert.Equal(1234, v.Amount);
        Assert.True(v.IsCritical);
        Assert.Equal("1234* energy {836045448940874}(reflected {1})", v.Raw);
    }
}

public class CombatLogFixtureTests
{
    private const ulong PlayerId = 690000000000001;
    internal static string FixturePath =>
        Path.Combine(Repo.Root, "tests", "combat-logs", "combat_2026-10-22_04_06_32_959698.txt");

    internal static List<CombatEvent> ParseFixture(out CombatLogSession session)
    {
        session = new CombatLogSession(FixturePath);
        var events = new List<CombatEvent>();
        foreach (var line in File.ReadLines(FixturePath, CombatLogFile.Encoding))
            if (session.Parse(line) is { } e) events.Add(e);
        return events;
    }

    [Fact]
    public void EveryLineParses_AndIsInTimeOrder()
    {
        var events = ParseFixture(out var session);

        Assert.Equal(1047, events.Count);
        Assert.Equal(0, session.FailedLines);
        Assert.Equal(new DateTime(2026, 10, 22, 4, 6, 44, 499), events[0].Timestamp);
        Assert.Equal(new DateTime(2026, 10, 22, 4, 25, 5, 581), events[^1].Timestamp);
        Assert.True(events.Zip(events.Skip(1)).All(p => p.First.Timestamp <= p.Second.Timestamp));
        Assert.Equal(3, events.Count(e => e.Value is { IsFullyParsed: false }));
        Assert.All(events.Where(e => e.Value is { IsFullyParsed: false }),
            e => Assert.Equal(CombatIds.AreaEntered, e.Effect.Type.Id));
        Assert.DoesNotContain(events, e => e.Source?.Kind == CombatEntityKind.Unknown || e.Target?.Kind == CombatEntityKind.Unknown);
    }

    [Fact]
    public void NonAsciiNamesDecodeCorrectly()
    {
        var events = ParseFixture(out _);
        Assert.All(events.Where(e => e.Source?.IsPlayer == true), e => Assert.Equal("Tëstplayer", e.Source!.Name));
    }

    [Fact]
    public void EventCountsByCategory()
    {
        var events = ParseFixture(out _);
        int Count(ulong type) => events.Count(e => e.Effect.Type.Id == type);

        Assert.Equal(551, Count(CombatIds.ApplyEffect));
        Assert.Equal(138, Count(CombatIds.RemoveEffect));
        Assert.Equal(240, Count(CombatIds.Event));
        Assert.Equal(53, Count(CombatIds.Restore));
        Assert.Equal(45, Count(CombatIds.ModifyCharges));
        Assert.Equal(14, Count(CombatIds.Spend));
        Assert.Equal(3, Count(CombatIds.AreaEntered));
        Assert.Equal(3, Count(CombatIds.DisciplineChanged));
        Assert.Equal(265, events.Count(e => e.IsDamage));
        Assert.Equal(119, events.Count(e => e.IsHeal));
        Assert.Equal(5, events.Count(e => e.IsEvent(CombatIds.EnterCombat)));
        Assert.Equal(5, events.Count(e => e.IsEvent(CombatIds.ExitCombat)));
        Assert.Equal(9, events.Count(e => e.IsEvent(CombatIds.Death)));
    }

    [Fact]
    public void DamageAndHealingTotals_MatchIndependentCount()
    {
        var events = ParseFixture(out _);
        var byPlayer = events.Where(e => e.IsDamage && e.Source is { IsPlayer: true, Id: PlayerId }).ToList();
        var byCompanion = events.Where(e => e.IsDamage && e.Source is { Kind: CombatEntityKind.Companion } s && s.Owner!.Id == PlayerId);
        var taken = events.Where(e => e.IsDamage && e.Target is { IsPlayer: true, Id: PlayerId }).ToList();
        var healsReceived = events.Where(e => e.IsHeal && e.Target is { IsPlayer: true, Id: PlayerId }).ToList();

        Assert.Equal(561951, byPlayer.Sum(e => e.Value!.Amount));
        Assert.Equal(548948, byPlayer.Sum(e => e.Value!.EffectiveOrAmount));
        Assert.Equal(52, byPlayer.Count(e => e.Value!.IsCritical));
        Assert.Equal(461207, byCompanion.Sum(e => e.Value!.Amount));
        Assert.Equal(64076, taken.Sum(e => e.Value!.Amount));
        Assert.Equal(6016, events.Where(e => e.IsDamage).Sum(e => e.Value!.Absorbed ?? 0));
        Assert.Equal(6, events.Count(e => e.IsDamage && e.Value!.Mitigation?.Id == CombatIds.Miss));
        Assert.Equal(253541, healsReceived.Sum(e => e.Value!.Amount));
        Assert.Equal(77104, healsReceived.Sum(e => e.Value!.EffectiveOrAmount));
    }

    [Fact]
    public void TrainingDummyFight_Totals()
    {
        var events = ParseFixture(out _);
        var start = events.Single(e => e.IsEvent(CombatIds.EnterCombat) && e.Timestamp.TimeOfDay == new TimeSpan(0, 4, 14, 39, 492));
        var end = events.First(e => e.IsEvent(CombatIds.ExitCombat) && e.Timestamp > start.Timestamp);
        var hits = events.Where(e => e.Timestamp >= start.Timestamp && e.Timestamp <= end.Timestamp && e.IsDamage &&
                                     e.Source!.IsOrBelongsTo(PlayerId)).ToList();

        Assert.Equal(140, hits.Count);
        Assert.Equal(844208, hits.Sum(e => e.Value!.Amount));
        Assert.Equal(844200, hits.Sum(e => e.Value!.EffectiveOrAmount));
        Assert.Equal(new TimeSpan(0, 0, 0, 44, 873), end.Timestamp - start.Timestamp);
    }

    [Fact]
    public async Task Replay_PublishesRawLinesAndParsedEvents()
    {
        var log = new TestLogger();
        var bus = new EventBus(log);
        var lines = new List<CombatLogLine>();
        var events = new List<CombatEvent>();
        bus.Subscribe<CombatLogLine>(lines.Add);
        bus.Subscribe<CombatEvent>(events.Add);

        await CombatLogSource.ReplayAsync(FixturePath, bus, log, CancellationToken.None);

        Assert.Equal(1047, lines.Count);
        Assert.Equal(1047, events.Count);
        Assert.Contains("Tëstplayer", lines[0].RawText);
        Assert.Empty(log.Warnings);
    }
}

public class CombatLogSessionTests
{
    private const string Line = "[TIME] [@Tëstplayer#690000000000001|(1.0,2.0,3.0,4.0)|(10/10)] [] [] [Event {836045448945472}: EnterCombat {836045448945489}]";

    private static string At(string time) => Line.Replace("TIME", time);

    [Fact]
    public void StartTimeComesFromFileName()
    {
        Assert.True(CombatLogFile.TryGetStartTime(@"C:\logs\combat_2026-10-22_04_06_32_959698.txt", out var start));
        Assert.Equal(new DateTime(2026, 10, 22, 4, 6, 32), start);
        Assert.False(CombatLogFile.TryGetStartTime("notes.txt", out _));
    }

    [Fact]
    public void DateAdvancesPastMidnight()
    {
        var session = new CombatLogSession("combat_2026-10-22_23_58_00_000000.txt");

        var before = session.Parse(At("23:59:59.900"))!;
        var after = session.Parse(At("00:00:00.100"))!;
        var later = session.Parse(At("00:05:00.000"))!;

        Assert.Equal(new DateTime(2026, 10, 22, 23, 59, 59, 900), before.Timestamp);
        Assert.Equal(new DateTime(2026, 10, 23, 0, 0, 0, 100), after.Timestamp);
        Assert.Equal(new DateTime(2026, 10, 23, 0, 5, 0), later.Timestamp);
    }

    [Fact]
    public void FirstLineAfterMidnight_InFileStartedBefore()
    {
        var session = new CombatLogSession("combat_2026-10-22_23_59_58_000000.txt");
        Assert.Equal(new DateTime(2026, 10, 23, 0, 0, 1), session.Parse(At("00:00:01.000"))!.Timestamp);
    }

    [Fact]
    public void CountsUnparseableLines()
    {
        var session = new CombatLogSession("x.txt", new DateTime(2026, 1, 1));
        Assert.Null(session.Parse("garbage"));
        Assert.Equal(1, session.FailedLines);
    }

    [Fact]
    public void LogEncodingIsWindows1252()
    {
        Assert.Equal("Tëstplayer û", CombatLogFile.Encoding.GetString([0x54, 0xEB, 0x73, 0x74, 0x70, 0x6C, 0x61, 0x79, 0x65, 0x72, 0x20, 0xFB]));
    }
}
