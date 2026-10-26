using Ashla.SDK;

namespace Ashla.Host;

internal enum LogLevel
{
    Info,
    Warn,
    Error
}

internal sealed record LogEntry(DateTime Time, LogLevel Level, string Scope, string Message)
{
    public string Format() => $"[{Time:HH:mm:ss}] {Level.ToString().ToUpperInvariant(),-5} {Scope}: {Message}";
}

internal sealed class HostLogger(string scope) : ILogger
{
    public void Info(string message) => LogHub.Write(LogLevel.Info, scope, message);
    public void Warn(string message) => LogHub.Write(LogLevel.Warn, scope, message);

    public void Error(string message, Exception? exception = null) =>
        LogHub.Write(LogLevel.Error, scope, exception is null ? message : $"{message}: {exception}");
}

internal static class LogHub
{
    private static readonly object Gate = new();
    private static TextWriter? _file;

    public static LogBuffer Recent { get; } = new(1000);

    public static string? FilePath { get; private set; }

    public static string? Directory { get; private set; }

    public static void StartFileLog(string directory, int keepDays = 7)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            foreach (var old in System.IO.Directory.EnumerateFiles(directory, "ashla-*.log"))
                if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-keepDays)) File.Delete(old);

            var path = Path.Combine(directory, $"ashla-{DateTime.Now:yyyy-MM-dd}.log");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            lock (Gate)
            {
                _file?.Dispose();
                _file = new StreamWriter(stream) { AutoFlush = true };
                FilePath = path;
                Directory = directory;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Write(LogLevel.Warn, "Host", $"File logging unavailable ({error.Message}).");
        }
    }

    public static void Write(LogLevel level, string scope, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, scope, message);
        var line = entry.Format();
        Recent.Add(entry);
        lock (Gate)
        {
            Console.WriteLine(line);
            try { _file?.WriteLine(line); }
            catch (IOException) { }
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _file?.Dispose();
            _file = null;
        }
    }
}

internal sealed class LogBuffer(int capacity)
{
    private readonly object _gate = new();
    private readonly Queue<LogEntry> _entries = new();
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public void Add(LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity) _entries.Dequeue();
            _version++;
        }
    }

    public LogEntry[] Snapshot()
    {
        lock (_gate) return [.. _entries];
    }
}
