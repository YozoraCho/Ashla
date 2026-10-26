using System.Text;
using Ashla.CombatLog;
using Ashla.Host.Settings;
using Ashla.SDK;

namespace Ashla.Host;

internal sealed record EncounterRecording(
    string Title,
    string? Area,
    string Character,
    string? Discipline,
    DateTime Start,
    DateTime End,
    DateTime FirstLine,
    double Damage,
    double Healing,
    double DamageTaken,
    IReadOnlyList<string> Lines);

internal sealed class EncounterRecorder(Action<EncounterRecording> finished)
{
    private readonly object _gate = new();
    private CombatEntity? _owner;
    private CombatEvent? _area;
    private CombatEvent? _discipline;
    private Recording? _current;

    public void Handle(CombatEvent e)
    {
        EncounterRecording? done = null;
        lock (_gate)
        {
            if (e.Source is { IsPlayer: true } player &&
                (_owner is null || e.Effect.Type.Id is CombatIds.AreaEntered or CombatIds.DisciplineChanged))
            {
                if (_owner is not null && _owner.Id != player.Id) _discipline = null;
                _owner = player;
            }
            if (_owner is null) return;
            bool fromOurSide = e.Source?.IsOrBelongsTo(_owner.Id) == true;
            bool toOurSide = e.Target?.IsOrBelongsTo(_owner.Id) == true;
            if (e.Effect.Type.Id == CombatIds.AreaEntered && fromOurSide) _area = e;
            if (e.Effect.Type.Id == CombatIds.DisciplineChanged && fromOurSide) _discipline = e;

            if (e.IsEvent(CombatIds.EnterCombat) && fromOurSide)
            {
                done = Close();
                _current = new Recording(e.Timestamp, _owner.Name, _area?.Effect.Effect.Name,
                    _discipline?.Effect.Detail?.Name ?? _discipline?.Effect.Effect.Name);
                foreach (var context in new[] { _area, _discipline })
                    if (context is not null && context.Timestamp <= e.Timestamp) _current.Add(context, false, false);
            }
            if (_current is null) return;
            _current.Add(e, fromOurSide, toOurSide);
            if (e.IsEvent(CombatIds.ExitCombat) && fromOurSide) done = Close() ?? done;
        }
        if (done is not null) finished(done);
    }

    public void Flush()
    {
        EncounterRecording? done;
        lock (_gate) done = Close();
        if (done is not null) finished(done);
    }

    private EncounterRecording? Close()
    {
        var current = _current;
        _current = null;
        return current is { HasNumbers: true } ? current.Finish() : null;
    }

    private sealed class Recording(DateTime start, string character, string? area, string? discipline)
    {
        private readonly List<string> _lines = [];
        private readonly Dictionary<ulong, (string Name, long MaxHealth, double Damage)> _opponents = [];
        private readonly DateTime _start = start;
        private DateTime _first = start, _last = start;
        private double _damage, _healing, _taken;

        public bool HasNumbers { get; private set; }

        public void Add(CombatEvent e, bool fromOurSide, bool toOurSide)
        {
            if (_lines.Count == 0 || e.Timestamp < _first) _first = e.Timestamp;
            _lines.Add(e.RawText);
            _last = e.Timestamp;
            if (e.IsDamage && e.Value is { } damage)
            {
                if (fromOurSide)
                {
                    _damage += damage.Amount ?? 0;
                    HasNumbers = true;
                    if (e.Target is { Kind: CombatEntityKind.Npc } npc) Note(npc, damage.Amount ?? 0);
                }
                if (toOurSide)
                {
                    _taken += damage.Amount ?? 0;
                    HasNumbers = true;
                    if (e.Source is { Kind: CombatEntityKind.Npc } attacker) Note(attacker, 0);
                }
            }
            else if (e.IsHeal && fromOurSide && e.Value is { } heal)
            {
                _healing += heal.EffectiveOrAmount ?? 0;
                HasNumbers = true;
            }
        }

        private void Note(CombatEntity npc, double damage)
        {
            var key = npc.InstanceId ?? npc.Id;
            var known = _opponents.GetValueOrDefault(key, (npc.Name, 0L, 0d));
            _opponents[key] = (npc.Name, Math.Max(known.Item2, npc.Health?.Max ?? 0), known.Item3 + damage);
        }

        public EncounterRecording Finish()
        {
            var hit = _opponents.Values.Where(o => o.Damage > 0).ToList();
            string title = hit.Count > 0 ? hit.MaxBy(o => (o.MaxHealth, o.Damage)).Name
                : _opponents.Count > 0 ? _opponents.Values.MaxBy(o => o.MaxHealth).Name
                : "Encounter";
            return new EncounterRecording(title, area, character, discipline, _start, _last, _first,
                _damage, _healing, _taken, [.. _lines]);
        }
    }
}

internal sealed class ArchiveIndex
{
    public int Version { get; set; } = 1;
    public List<ArchiveEntry> Encounters { get; set; } = [];
}

internal sealed class ArchiveEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Area { get; set; }
    public string Character { get; set; } = "";
    public string? Discipline { get; set; }
    public DateTime Start { get; set; }
    public double Seconds { get; set; }
    public double Damage { get; set; }
    public double Healing { get; set; }
    public double DamageTaken { get; set; }

    public SavedEncounter ToSaved() => new(Id, Title, Area, Character, Discipline, Start, TimeSpan.FromSeconds(Seconds),
        Damage, Healing, DamageTaken);
}

internal sealed record ImportResult(int Found, int Added, int AlreadySaved, int UnreadLines)
{
    public string Describe(string file) =>
        Found == 0 ? $"No fights in {file}."
        : Added == 0 ? $"The {Plural(Found, "fight")} in {file} {(Found == 1 ? "was" : "were")} already saved."
        : $"Saved {Plural(Added, "fight")} from {file}" + (AlreadySaved > 0 ? $" ({AlreadySaved} already saved)." : ".");

    public static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    public static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes"
    };
}

internal sealed class EncounterArchive : IEncounterArchive
{
    public const string IndexFile = "index.json";

    private readonly object _gate = new();
    private readonly ILogger _log;
    private readonly Func<int> _keepDays;
    private readonly Func<DateTime> _now;
    private ArchiveIndex _index;

    public EncounterArchive(string directory, ILogger log, Func<int> keepDays, Func<DateTime>? now = null)
    {
        Directory = directory;
        _log = log;
        _keepDays = keepDays;
        _now = now ?? (() => DateTime.Now);
        _index = JsonFile.Load<ArchiveIndex>(Path.Combine(directory, IndexFile), log, "the list of saved fights");
    }

    public string Directory { get; }

    public bool IsAvailable => true;

    public event Action? Changed;

    public IReadOnlyList<SavedEncounter> List()
    {
        lock (_gate) return [.. _index.Encounters.OrderByDescending(e => e.Start).Select(e => e.ToSaved())];
    }

    public int Count
    {
        get { lock (_gate) return _index.Encounters.Count; }
    }

    public long SizeOnDisk()
    {
        try
        {
            return System.IO.Directory.Exists(Directory)
                ? new DirectoryInfo(Directory).EnumerateFiles("*.txt").Sum(f => f.Length)
                : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public IReadOnlyList<CombatEvent> Load(string id)
    {
        var path = PathFor(id);
        if (path is null || !File.Exists(path)) return [];
        var session = new CombatLogSession(path);
        var events = new List<CombatEvent>();
        try
        {
            foreach (var line in File.ReadLines(path, CombatLogFile.Encoding))
                if (!string.IsNullOrWhiteSpace(line) && session.Parse(line) is { } e) events.Add(e);
        }
        catch (IOException error)
        {
            _log.Warn($"Couldn't read saved fight {id}: {error.Message}");
        }
        return events;
    }

    public bool Save(EncounterRecording recording)
    {
        string id;
        lock (_gate)
        {
            if (_index.Encounters.Any(e => e.Start == recording.Start && e.Character == recording.Character)) return false;
            id = NewId(recording.FirstLine);
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllLines(Path.Combine(Directory, id + ".txt"), recording.Lines, CombatLogFile.Encoding);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _log.Warn($"Couldn't save the fight against {recording.Title}: {error.Message}");
                return false;
            }
            _index.Encounters.Add(new ArchiveEntry
            {
                Id = id,
                Title = recording.Title,
                Area = recording.Area,
                Character = recording.Character,
                Discipline = recording.Discipline,
                Start = recording.Start,
                Seconds = Math.Round((recording.End - recording.Start).TotalSeconds, 3),
                Damage = recording.Damage,
                Healing = recording.Healing,
                DamageTaken = recording.DamageTaken
            });
            PruneLocked();
            SaveIndexLocked();
        }
        Changed?.Invoke();
        return true;
    }

    public ImportResult Import(string logFile)
    {
        var session = new CombatLogSession(logFile);
        var recordings = new List<EncounterRecording>();
        var recorder = new EncounterRecorder(recordings.Add);
        foreach (var line in File.ReadLines(logFile, CombatLogFile.Encoding))
            if (!string.IsNullOrWhiteSpace(line) && session.Parse(line) is { } e) recorder.Handle(e);
        recorder.Flush();
        int added = 0;
        foreach (var recording in recordings)
            if (Save(recording)) added++;
        _log.Info($"Opened {Path.GetFileName(logFile)}: {recordings.Count} fight(s), {added} new.");
        return new ImportResult(recordings.Count, added, recordings.Count - added, session.FailedLines);
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            if (_index.Encounters.RemoveAll(e => e.Id == id) == 0) return false;
            DeleteFile(id);
            SaveIndexLocked();
        }
        Changed?.Invoke();
        return true;
    }

    public int DeleteAll()
    {
        int count;
        lock (_gate)
        {
            count = _index.Encounters.Count;
            foreach (var entry in _index.Encounters) DeleteFile(entry.Id);
            _index.Encounters.Clear();
            SaveIndexLocked();
        }
        if (count > 0) Changed?.Invoke();
        return count;
    }

    public int Prune()
    {
        int removed;
        lock (_gate)
        {
            removed = PruneLocked();
            if (removed > 0) SaveIndexLocked();
        }
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    private int PruneLocked()
    {
        int days = _keepDays();
        if (days <= 0) return 0;
        var cutoff = _now().AddDays(-days);
        var old = _index.Encounters.Where(e => e.Start < cutoff).ToList();
        foreach (var entry in old)
        {
            DeleteFile(entry.Id);
            _index.Encounters.Remove(entry);
        }
        if (old.Count > 0) _log.Info($"Removed {old.Count} saved fight(s) older than {days} days.");
        return old.Count;
    }

    private string NewId(DateTime firstLine)
    {
        var baseId = $"combat_{firstLine:yyyy-MM-dd_HH_mm_ss}_{firstLine:fff}000";
        var id = baseId;
        for (int n = 2; _index.Encounters.Any(e => e.Id == id) || File.Exists(Path.Combine(Directory, id + ".txt")); n++)
            id = $"{baseId}_{n}";
        return id;
    }

    private string? PathFor(string id)
    {
        if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..")) return null;
        return Path.Combine(Directory, id + ".txt");
    }

    private void DeleteFile(string id)
    {
        try
        {
            if (PathFor(id) is { } path && File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Couldn't delete saved fight {id}: {error.Message}");
        }
    }

    private void SaveIndexLocked()
    {
        System.IO.Directory.CreateDirectory(Directory);
        JsonFile.Save(Path.Combine(Directory, IndexFile), _index, _log, "the list of saved fights");
    }
}

internal sealed class PluginEncounterArchive(IEncounterArchive? archive) : IEncounterArchive
{
    private readonly object _gate = new();
    private readonly List<Action> _handlers = [];
    private bool _closed;

    public bool IsAvailable => archive is not null && !_closed;

    public IReadOnlyList<SavedEncounter> List() => IsAvailable ? archive!.List() : [];

    public IReadOnlyList<CombatEvent> Load(string id) => IsAvailable ? archive!.Load(id) : [];

    public event Action? Changed
    {
        add
        {
            if (value is null || archive is null) return;
            lock (_gate)
            {
                if (_closed) return;
                _handlers.Add(value);
                archive.Changed += value;
            }
        }
        remove
        {
            if (value is null || archive is null) return;
            lock (_gate)
            {
                if (_handlers.Remove(value)) archive.Changed -= value;
            }
        }
    }

    public int Close()
    {
        lock (_gate)
        {
            _closed = true;
            if (archive is not null)
                foreach (var handler in _handlers) archive.Changed -= handler;
            int count = _handlers.Count;
            _handlers.Clear();
            return count;
        }
    }
}
