using Ashla.SDK;

namespace Ashla.Host;

internal sealed class Toast(long id, string title, string? text, ToastKind kind, string source)
{
    public const float FadeIn = 0.2f, FadeOut = 0.4f;

    public long Id { get; } = id;
    public string Title { get; } = title;
    public string? Text { get; } = text;
    public ToastKind Kind { get; } = kind;

    public string Source { get; } = source;

    public float Age { get; internal set; }

    public int Count { get; internal set; } = 1;

    public float Duration => Kind switch
    {
        ToastKind.Error => 12f,
        ToastKind.Warning => 9f,
        _ => 6f
    };

    public bool Expired => Age >= Duration;

    public float Opacity => Math.Clamp(Math.Min(Age / FadeIn, (Duration - Age) / FadeOut), 0f, 1f);
}

internal sealed class ToastCenter
{
    public const int MaxVisible = 4;
    public const int MaxWaiting = 20;
    public const int MaxTitleLength = 80;
    public const int MaxTextLength = 300;

    public const float MaxStep = 0.1f;

    private readonly object _gate = new();
    private readonly List<Toast> _waiting = [];
    private readonly List<Toast> _visible = [];
    private long _nextId;

    public void Show(string title, string? text, ToastKind kind, string source = "Ashla")
    {
        title = Shorten(title, MaxTitleLength) ?? "";
        text = Shorten(text, MaxTextLength);
        if (title.Length == 0) return;
        lock (_gate)
        {
            var same = _visible.Concat(_waiting).FirstOrDefault(t =>
                t.Title == title && t.Text == text && t.Kind == kind && t.Source == source);
            if (same is not null)
            {
                same.Age = Math.Min(same.Age, Toast.FadeIn);
                same.Count++;
                return;
            }
            _waiting.Add(new Toast(++_nextId, title, text, kind, source));
            if (_waiting.Count > MaxWaiting) _waiting.RemoveAt(0);
        }
    }

    public IReadOnlyList<Toast> Update(float seconds, long? hoveredId = null)
    {
        float step = Math.Clamp(seconds, 0f, MaxStep);
        lock (_gate)
        {
            foreach (var toast in _visible)
                if (toast.Id != hoveredId || toast.Age >= toast.Duration - Toast.FadeOut) toast.Age += step;
            _visible.RemoveAll(t => t.Expired);
            while (_visible.Count < MaxVisible && _waiting.Count > 0)
            {
                _visible.Add(_waiting[0]);
                _waiting.RemoveAt(0);
            }
            return [.. _visible];
        }
    }

    public void Dismiss(long id)
    {
        lock (_gate)
        {
            var toast = _visible.FirstOrDefault(t => t.Id == id);
            if (toast is not null) toast.Age = Math.Max(toast.Age, toast.Duration - Toast.FadeOut);
        }
    }

    public int WaitingCount
    {
        get { lock (_gate) return _waiting.Count; }
    }

    private static string? Shorten(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim().ReplaceLineEndings(" ");
        return value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "\u2026";
    }
}

internal sealed class PluginToasts(ToastCenter? center, string pluginName, ILogger log) : IToasts
{
    public void Show(string title, string? text = null, ToastKind kind = ToastKind.Info)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string line = text is null ? $"Toast: {title}" : $"Toast: {title} ({text})";
        if (kind is ToastKind.Warning or ToastKind.Error) log.Warn(line);
        else log.Info(line);
        center?.Show(title, text, kind, pluginName);
    }
}
