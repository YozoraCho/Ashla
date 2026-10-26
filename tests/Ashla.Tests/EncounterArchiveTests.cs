using Ashla.CombatLog;
using Ashla.Host;
using Ashla.SDK;

namespace Ashla.Tests;

public class EncounterArchiveTests : IDisposable
{
    private readonly TestLogger _log = new();
    private readonly TempDirectory _temp = new();
    private int _keepDays = 30;
    private DateTime _now = new(2026, 10, 23, 12, 0, 0);

    public void Dispose() => _temp.Dispose();

    private EncounterArchive NewArchive() =>
        new(Path.Combine(_temp.Path, "encounters"), _log, () => _keepDays, () => _now);

    [Fact]
    public void Import_SplitsTheLogIntoFights_AndSkipsOnesAlreadySaved()
    {
        var archive = NewArchive();
        int changes = 0;
        archive.Changed += () => changes++;

        var first = archive.Import(CombatLogFixtureTests.FixturePath);
        var again = archive.Import(CombatLogFixtureTests.FixturePath);

        Assert.Equal(new ImportResult(5, 5, 0, 0), first);
        Assert.Equal(new ImportResult(5, 0, 5, 0), again);
        Assert.Equal(5, changes);
        var saved = archive.List();
        Assert.Equal(5, saved.Count);
        Assert.Equal("Combat Training Target", saved[0].Title);
        Assert.True(saved[0].Start > saved[1].Start);
        Assert.Equal(844208, saved[0].Damage);
        Assert.Equal(new TimeSpan(0, 0, 0, 44, 873), saved[0].Duration);
        Assert.Equal("Concentration", saved[0].Discipline);
        Assert.StartsWith("T", saved[0].Character);
    }

    [Fact]
    public void SavedFight_LoadsBack_AndGivesTheSameNumbers()
    {
        var archive = NewArchive();
        archive.Import(CombatLogFixtureTests.FixturePath);
        var dummy = archive.List()[0];

        var events = archive.Load(dummy.Id);
        double damage = events.Where(e => e.IsDamage && e.Source?.IsOrBelongsTo(690000000000001) == true).Sum(e => e.Value?.Amount ?? 0);

        Assert.Equal(844208, damage);
        Assert.Equal(dummy.Damage, damage);
        Assert.Equal(dummy.Start, events.First(e => e.IsEvent(CombatIds.EnterCombat)).Timestamp);
        Assert.Equal("Combat Training Target", dummy.Title);
        Assert.Equal("Concentration", dummy.Discipline);
        Assert.Contains(events, e => e.Effect.Type.Id == CombatIds.AreaEntered);
    }

    [Fact]
    public void Archive_SurvivesARestart()
    {
        NewArchive().Import(CombatLogFixtureTests.FixturePath);
        var reopened = NewArchive();
        Assert.Equal(5, reopened.List().Count);
        Assert.NotEmpty(reopened.Load(reopened.List()[2].Id));
    }

    [Fact]
    public void OldFights_ArePruned_UnlessKeptForever()
    {
        var archive = NewArchive();
        archive.Import(CombatLogFixtureTests.FixturePath);
        _now = new DateTime(2026, 12, 1);
        _keepDays = 0;
        Assert.Equal(0, archive.Prune());
        _keepDays = 30;
        Assert.Equal(5, archive.Prune());
        Assert.Empty(archive.List());
        Assert.Empty(Directory.GetFiles(archive.Directory, "*.txt"));
    }

    [Fact]
    public void Delete_And_DeleteAll()
    {
        var archive = NewArchive();
        archive.Import(CombatLogFixtureTests.FixturePath);
        var id = archive.List()[1].Id;
        Assert.True(archive.Delete(id));
        Assert.False(archive.Delete(id));
        Assert.Empty(archive.Load(id));
        Assert.Equal(4, archive.DeleteAll());
        Assert.Empty(archive.List());
        Assert.Empty(archive.Load("..\\..\\secrets"));
    }

    [Fact]
    public void Recorder_SavesEachFightAsItEnds()
    {
        var recordings = new List<EncounterRecording>();
        var recorder = new EncounterRecorder(recordings.Add);
        var events = CombatLogFixtureTests.ParseFixture(out _);
        foreach (var e in events.Take(events.FindIndex(e => e.IsEvent(CombatIds.ExitCombat)) + 1)) recorder.Handle(e);

        var first = Assert.Single(recordings);
        Assert.Equal("Separatist Siege Gunner", first.Title);
        Assert.Equal(309, first.Damage);
        Assert.StartsWith("[", first.Lines[0]);
        Assert.Contains("AreaEntered", first.Lines[0]);
        recorder.Flush();
        Assert.Single(recordings);
    }

    [Fact]
    public void PluginArchive_ForgetsHandlersWhenThePluginUnloads()
    {
        var archive = NewArchive();
        var scoped = new PluginEncounterArchive(archive);
        int calls = 0;
        scoped.Changed += () => calls++;
        archive.Import(CombatLogFixtureTests.FixturePath);
        Assert.Equal(5, calls);

        Assert.Equal(1, scoped.Close());
        archive.DeleteAll();
        Assert.Equal(5, calls);
        Assert.False(scoped.IsAvailable);
        Assert.Empty(scoped.List());
        Assert.False(new PluginEncounterArchive(null).IsAvailable);
    }
}
