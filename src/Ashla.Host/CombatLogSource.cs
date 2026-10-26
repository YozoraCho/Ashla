using Ashla.CombatLog;
using Ashla.SDK;

namespace Ashla.Host;

internal static class CombatLogSource
{
    public static async Task ReplayAsync(string file, IEventBus events, ILogger log, CancellationToken cancellationToken)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("Combat log not found", file);
        using var reader = new StreamReader(file, CombatLogFile.Encoding);
        var session = new CombatLogSession(file);
        int parsed = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
            if (!string.IsNullOrWhiteSpace(line) && PublishLine(line, session, events, log))
                parsed++;
        log.Info($"Replayed {parsed} combat event(s) from {Path.GetFileName(file)}" +
                 (session.FailedLines > 0 ? $"; {session.FailedLines} line(s) weren't recognized." : "."));
    }

    internal static bool PublishLine(string line, CombatLogSession session, IEventBus events, ILogger log)
    {
        events.Publish(new CombatLogLine(DateTimeOffset.Now, session.SourceFile, line));
        var combatEvent = session.Parse(line);
        if (combatEvent is null)
        {
            if (session.FailedLines == 1)
                log.Warn($"Unrecognized combat-log line (raw line still published): {Truncate(line)}");
            return false;
        }
        events.Publish(combatEvent);
        return true;
    }

    private static string Truncate(string text) => text.Length <= 160 ? text : text[..160] + "...";

    public static async Task WatchAsync(string directory, IEventBus events, ILogger log, CancellationToken token,
        CombatLogStatus? status = null)
    {
        status?.SetWatching(directory);
        log.Info($"Watching for NEW combat-log lines in {directory}");
        string? currentFile = null;
        CombatLogSession? session = null;
        long position = 0;
        using var partialLine = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        bool warnedMissing = false;
        bool firstScan = true;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    status?.SetDirectoryExists(false);
                    if (!warnedMissing)
                        log.Warn("Combat-log folder not found. Enable Preferences > Combat Logging in SWTOR; " +
                                 "Ashla will pick it up once the game creates the folder.");
                    warnedMissing = true;
                    await Task.Delay(500, token);
                    continue;
                }
                warnedMissing = false;
                status?.SetDirectoryExists(true);

                var latest = Directory.EnumerateFiles(directory)
                    .Where(p => Path.GetExtension(p).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                             || Path.GetExtension(p).Equals(".log", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                bool startup = firstScan;
                firstScan = false;

                if (latest is null)
                {
                    await Task.Delay(500, token);
                    continue;
                }

                if (!string.Equals(currentFile, latest, StringComparison.OrdinalIgnoreCase))
                {
                    currentFile = latest;
                    session = new CombatLogSession(latest);
                    position = startup ? new FileInfo(latest).Length : 0;
                    partialLine.SetLength(0);
                    log.Info($"Active log: {Path.GetFileName(latest)} ({(startup ? "tailing from end" : "new file, reading from the start")})");
                    status?.SetActiveFile(latest);
                }

                await using var stream = new FileStream(latest, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);

                if (stream.Length < position)
                {
                    position = 0;
                    partialLine.SetLength(0);
                }
                stream.Position = position;
                int count;
                while ((count = await stream.ReadAsync(chunk, token)) > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (chunk[i] == (byte)'\n')
                        {
                            var text = CombatLogFile.Encoding.GetString(partialLine.ToArray()).TrimEnd('\r');
                            partialLine.SetLength(0);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                PublishLine(text, session!, events, log);
                                status?.LineReceived();
                            }
                        }
                        else if (partialLine.Length < 1024 * 1024) partialLine.WriteByte(chunk[i]);
                        else partialLine.SetLength(0);
                    }
                }
                position = stream.Position;
                await Task.Delay(350, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (IOException error)
            {
                log.Warn($"Log temporarily unavailable: {error.Message}");
                await Task.Delay(600, token);
            }
        }
    }
}

internal sealed class CombatLogStatus
{
    private readonly object _gate = new();
    private string? _directory;
    private bool? _directoryExists;
    private string? _activeFile;
    private DateTime? _lastLine;

    public event Action<bool>? DirectoryExistsChanged;

    public string? Directory { get { lock (_gate) return _directory; } }

    public bool? DirectoryExists { get { lock (_gate) return _directoryExists; } }

    public string? ActiveFile { get { lock (_gate) return _activeFile; } }

    public DateTime? LastLine { get { lock (_gate) return _lastLine; } }

    public void SetWatching(string directory)
    {
        lock (_gate) _directory = directory;
    }

    public void SetDirectoryExists(bool exists)
    {
        bool changed;
        lock (_gate)
        {
            changed = _directoryExists != exists;
            _directoryExists = exists;
        }
        if (changed) DirectoryExistsChanged?.Invoke(exists);
    }

    public void SetActiveFile(string path)
    {
        lock (_gate) _activeFile = path;
    }

    public void LineReceived()
    {
        lock (_gate) _lastLine = DateTime.Now;
    }
}
