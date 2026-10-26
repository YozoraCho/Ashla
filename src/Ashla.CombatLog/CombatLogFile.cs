using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ashla.SDK;

namespace Ashla.CombatLog;

public static partial class CombatLogFile
{
    public static Encoding Encoding { get; } = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    public static bool TryGetStartTime(string path, out DateTime start)
    {
        start = default;
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        var match = FileNameRegex().Match(name);
        return match.Success && DateTime.TryParseExact(
            $"{match.Groups["date"].Value} {match.Groups["time"].Value.Replace('_', ':')}",
            "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out start);
    }

    [GeneratedRegex(@"^combat_(?<date>\d{4}-\d{2}-\d{2})_(?<time>\d{2}_\d{2}_\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();
}

public sealed class CombatLogSession
{
    private static readonly TimeSpan RolloverThreshold = TimeSpan.FromHours(6);

    private DateTime _date;
    private TimeSpan _lastTimeOfDay;

    public CombatLogSession(string sourceFile, DateTime? start = null)
    {
        SourceFile = sourceFile;
        var begin = start
                    ?? (CombatLogFile.TryGetStartTime(sourceFile, out var fromName) ? fromName : (DateTime?)null)
                    ?? (File.Exists(sourceFile) ? File.GetCreationTime(sourceFile) : DateTime.Now);
        _date = begin.Date;
        _lastTimeOfDay = begin.TimeOfDay;
    }

    public string SourceFile { get; }

    public int FailedLines { get; private set; }

    public CombatEvent? Parse(string line)
    {
        if (!CombatLogParser.TryParse(line, _date, SourceFile, out var parsed) || parsed is null)
        {
            FailedLines++;
            return null;
        }

        var timeOfDay = parsed.Timestamp.TimeOfDay;
        if (timeOfDay < _lastTimeOfDay - RolloverThreshold)
        {
            _date = _date.AddDays(1);
            parsed = parsed with { Timestamp = _date + timeOfDay };
        }
        _lastTimeOfDay = timeOfDay;
        return parsed;
    }
}
