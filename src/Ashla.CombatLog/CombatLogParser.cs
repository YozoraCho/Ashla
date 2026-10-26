using System.Globalization;
using System.Text.RegularExpressions;
using Ashla.SDK;

namespace Ashla.CombatLog;

public static partial class CombatLogParser
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static bool TryParse(string line, DateTime date, string sourceFile, out CombatEvent? combatEvent)
    {
        combatEvent = null;
        var match = LineRegex().Match(line);
        if (!match.Success) return false;
        if (!TryParseTimeOfDay(match.Groups["time"].Value, out var timeOfDay)) return false;
        if (!TryParseEffect(match.Groups["effect"].Value, out var effect)) return false;

        var source = ParseEntity(match.Groups["source"].Value, sameAs: null);
        var target = ParseEntity(match.Groups["target"].Value, sameAs: source);
        var ability = match.Groups["ability"].Value is { Length: > 0 } abilityText ? ParseRef(abilityText) : null;
        var value = match.Groups["value"].Success ? ParseValue(match.Groups["value"].Value) : null;

        double? threat = null;
        string? annotation = null;
        if (match.Groups["threat"].Success)
        {
            var text = match.Groups["threat"].Value;
            if (double.TryParse(text, NumberStyles.Float, Invariant, out var number)) threat = number;
            else annotation = text;
        }

        combatEvent = new CombatEvent(date.Date + timeOfDay, source, target, ability, effect, value, threat,
            annotation, sourceFile, line);
        return true;
    }

    internal static bool TryParseTimeOfDay(string text, out TimeSpan timeOfDay) =>
        TimeSpan.TryParseExact(text, @"hh\:mm\:ss\.fff", Invariant, out timeOfDay);

    internal static CombatEntity? ParseEntity(string text, CombatEntity? sameAs)
    {
        if (text.Length == 0) return null;
        if (text == "=") return sameAs;

        var parts = text.Split('|');
        CombatPosition? position = parts.Length > 1 ? ParsePosition(parts[1]) : null;
        CombatHealth? health = parts.Length > 2 ? ParseHealth(parts[2]) : null;
        var head = parts[0];

        if (head.StartsWith('@'))
        {
            var body = head[1..];
            int slash = body.IndexOf('/');
            var player = ParsePlayer(slash < 0 ? body : body[..slash]);
            if (slash < 0) return player with { Position = position, Health = health };

            var companion = ParseNpcHead(body[(slash + 1)..]);
            return companion is null
                ? new CombatEntity(CombatEntityKind.Unknown, head, 0, null, player, position, health)
                : new CombatEntity(CombatEntityKind.Companion, companion.Value.Name, companion.Value.Id,
                    companion.Value.InstanceId, player, position, health);
        }

        var npc = ParseNpcHead(head);
        return npc is null
            ? new CombatEntity(CombatEntityKind.Unknown, head, 0, null, null, position, health)
            : new CombatEntity(CombatEntityKind.Npc, npc.Value.Name, npc.Value.Id, npc.Value.InstanceId, null,
                position, health);
    }

    private static CombatEntity ParsePlayer(string text)
    {
        int hash = text.LastIndexOf('#');
        ulong id = 0;
        var name = text;
        if (hash >= 0 && ulong.TryParse(text.AsSpan(hash + 1), NumberStyles.None, Invariant, out id))
            name = text[..hash];
        return new CombatEntity(CombatEntityKind.Player, name, id, null, null, null, null);
    }

    private static (string Name, ulong Id, ulong? InstanceId)? ParseNpcHead(string text)
    {
        var match = NpcRegex().Match(text);
        if (!match.Success) return null;
        ulong? instance = match.Groups["instance"].Success
            ? ulong.Parse(match.Groups["instance"].ValueSpan, NumberStyles.None, Invariant)
            : null;
        return (match.Groups["name"].Value, ulong.Parse(match.Groups["id"].ValueSpan, NumberStyles.None, Invariant), instance);
    }

    private static CombatPosition? ParsePosition(string text)
    {
        var numbers = ParseNumberList(text, ',');
        return numbers is { Length: 4 } ? new CombatPosition(numbers[0], numbers[1], numbers[2], numbers[3]) : null;
    }

    private static CombatHealth? ParseHealth(string text)
    {
        if (text.Length < 5 || text[0] != '(' || text[^1] != ')') return null;
        var inner = text.AsSpan(1, text.Length - 2);
        int slash = inner.IndexOf('/');
        if (slash < 0) return null;
        return long.TryParse(inner[..slash], NumberStyles.AllowLeadingSign, Invariant, out var current) &&
               long.TryParse(inner[(slash + 1)..], NumberStyles.AllowLeadingSign, Invariant, out var max)
            ? new CombatHealth(current, max)
            : null;
    }

    private static double[]? ParseNumberList(string text, char separator)
    {
        if (text.Length < 2 || text[0] != '(' || text[^1] != ')') return null;
        var parts = text[1..^1].Split(separator);
        var values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, Invariant, out values[i])) return null;
        return values;
    }

    internal static bool TryParseEffect(string text, out CombatEffect effect)
    {
        effect = null!;
        int split = text.IndexOf("}: ", StringComparison.Ordinal);
        if (split < 0) return false;
        var type = ParseRef(text[..(split + 1)]);
        if (type is null) return false;
        var rest = text[(split + 3)..];

        int slash = rest.IndexOf("}/", StringComparison.Ordinal);
        if (slash >= 0 && ParseRef(rest[..(slash + 1)]) is { } main && ParseRef(rest[(slash + 2)..]) is { } detail)
        {
            effect = new CombatEffect(type, main, detail);
            return true;
        }

        if (ParseRef(rest) is { } single)
        {
            effect = new CombatEffect(type, single);
            return true;
        }

        int firstClose = rest.IndexOf('}');
        if (firstClose > 0 && ParseRef(rest[..(firstClose + 1)]) is { } first &&
            ParseRef(rest[(firstClose + 1)..].Trim()) is { } second)
        {
            effect = new CombatEffect(type, first, second);
            return true;
        }
        return false;
    }

    internal static GameRef? ParseRef(string text)
    {
        var match = RefRegex().Match(text);
        return match.Success
            ? new GameRef(match.Groups["name"].Value, ulong.Parse(match.Groups["id"].ValueSpan, NumberStyles.None, Invariant))
            : null;
    }

    internal static CombatValue ParseValue(string text)
    {
        var match = ValueRegex().Match(text);
        if (match.Success)
        {
            return new CombatValue(
                Amount: Number(match.Groups["amount"]),
                IsCritical: match.Groups["crit"].Success,
                Effective: Number(match.Groups["effective"]),
                Kind: Ref(match.Groups["kind"], match.Groups["kindId"]),
                Mitigation: Ref(match.Groups["mitigation"], match.Groups["mitigationId"]),
                HasUnnamedMitigation: match.Groups["dash"].Success && !match.Groups["mitigationId"].Success,
                Absorbed: Number(match.Groups["absorbed"]),
                Raw: text,
                IsFullyParsed: true);
        }

        var lead = LeadingAmountRegex().Match(text);
        return new CombatValue(
            Amount: lead.Success ? Number(lead.Groups["amount"]) : null,
            IsCritical: lead.Success && lead.Groups["crit"].Success,
            Effective: lead.Success ? Number(lead.Groups["effective"]) : null,
            Kind: null, Mitigation: null, HasUnnamedMitigation: false, Absorbed: null,
            Raw: text, IsFullyParsed: false);

        static double? Number(Group group) =>
            group.Success && double.TryParse(group.ValueSpan, NumberStyles.Float, Invariant, out var n) ? n : null;

        static GameRef? Ref(Group name, Group id) =>
            id.Success ? new GameRef(name.Value, ulong.Parse(id.ValueSpan, NumberStyles.None, Invariant)) : null;
    }

    [GeneratedRegex(@"^\[(?<time>[^\]]*)\] \[(?<source>[^\]]*)\] \[(?<target>[^\]]*)\] \[(?<ability>[^\]]*)\] \[(?<effect>[^\]]*)\](?: \((?<value>.*?)\))?(?: <(?<threat>[^>]*)>)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    [GeneratedRegex(@"^(?<name>[^{}]*?) \{(?<id>\d+)\}(?::(?<instance>\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NpcRegex();

    [GeneratedRegex(@"^(?<name>[^{}]*?)\s*\{(?<id>\d+)\}$", RegexOptions.CultureInvariant)]
    private static partial Regex RefRegex();

    [GeneratedRegex(@"^(?<amount>-?\d+(?:\.\d+)?)(?<crit>\*)?(?: ~(?<effective>-?\d+(?:\.\d+)?))?(?: (?<kind>[^{}()\-][^{}()]*?) \{(?<kindId>\d+)\})?(?: (?<dash>-)(?<mitigation>[^{}()\s]*)(?: \{(?<mitigationId>\d+)\})?)?(?: \((?<absorbed>\d+(?:\.\d+)?) [^{}()]* \{\d+\}\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex ValueRegex();

    [GeneratedRegex(@"^(?<amount>-?\d+(?:\.\d+)?)(?<crit>\*)?(?: ~(?<effective>-?\d+(?:\.\d+)?))?", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingAmountRegex();
}
