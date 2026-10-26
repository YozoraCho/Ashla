using Ashla.SDK;

namespace Ashla.Host.Overlays;

internal interface IPanelChrome
{
    bool Begin(PanelEntry panel, string title, ref bool open);

    void End();
}

internal sealed class PanelRegistry(ILogger log, bool isAvailable, ToastCenter? toasts = null)
{
    private readonly object _gate = new();
    private readonly List<PanelEntry> _entries = [];
    private int _nextOrder;

    public bool IsAvailable { get; } = isAvailable;

    public PanelEntry Register(PluginInfo owner, IPluginPanel panel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(panel);
        if (!IsAvailable)
            throw new NotSupportedException("This host has no UI renderer. Start Ashla with --gui to use plugin panels.");

        var panelId = panel.Id;
        if (string.IsNullOrWhiteSpace(panelId))
            throw new ArgumentException("Panel Id must be a non-empty string.", nameof(panel));

        lock (_gate)
        {
            if (_entries.Any(e => e.Owner.Id == owner.Id && e.PanelId == panelId))
                throw new InvalidOperationException($"Plugin '{owner.Id}' already registered a panel with id '{panelId}'.");

            var entry = new PanelEntry(this, owner, panel, panelId, _nextOrder++) { LastTitle = TitleOf(panel) ?? panelId };
            _entries.Add(entry);
            return entry;
        }
    }

    private static string? TitleOf(IPluginPanel panel)
    {
        try
        {
            return string.IsNullOrWhiteSpace(panel.Title) ? null : panel.Title;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<PanelEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }

    public int RemoveAll(string ownerId)
    {
        PanelEntry[] owned;
        lock (_gate) owned = [.. _entries.Where(e => e.Owner.Id == ownerId)];
        foreach (var entry in owned) entry.Dispose();
        return owned.Length;
    }

    public void RenderFrame(IPanelChrome chrome)
    {
        foreach (var entry in Snapshot())
        {
            if (!entry.ShouldDraw) continue;
            lock (entry.DrawGate)
            {
                if (!entry.ShouldDraw) continue;
                DrawOne(chrome, entry);
            }
        }
    }

    private void DrawOne(IPanelChrome chrome, PanelEntry entry)
    {
        string title;
        try
        {
            title = entry.Panel.Title;
            if (string.IsNullOrWhiteSpace(title)) title = entry.PanelId;
        }
        catch (Exception error)
        {
            Fault(entry, error);
            return;
        }

        bool open = true;
        bool begun = false;
        try
        {
            bool contentVisible = chrome.Begin(entry, title, ref open);
            begun = true;
            if (contentVisible) entry.Panel.Draw();
        }
        catch (Exception error)
        {
            Fault(entry, error, title);
        }
        finally
        {
            if (begun) chrome.End();
        }

        entry.LastTitle = title;
        if (!open) entry.IsVisible = false;
    }

    private void Fault(PanelEntry entry, Exception error, string? title = null)
    {
        entry.Fault = error;
        log.Error($"Panel '{entry.PanelId}' from plugin '{entry.Owner.Id}' threw and has been suspended. " +
                  "Use the plugin manager to retry it.", error);
        toasts?.Show($"{title ?? entry.LastTitle ?? entry.PanelId} stopped working",
            "It hit an error and was paused. The Plugin Manager can try it again.", ToastKind.Error, entry.Owner.Name);
    }

    internal void Remove(PanelEntry entry)
    {
        lock (_gate) _entries.Remove(entry);
    }
}

internal sealed class PanelEntry : IPanelRegistration
{
    private readonly PanelRegistry _registry;
    private volatile bool _visible = true;
    private volatile bool _registered = true;
    private volatile Exception? _fault;

    internal PanelEntry(PanelRegistry registry, PluginInfo owner, IPluginPanel panel, string panelId, int order)
    {
        _registry = registry;
        Owner = owner;
        Panel = panel;
        PanelId = panelId;
        Order = order;
        LastTitle = panelId;
    }

    internal object DrawGate { get; } = new();
    public PluginInfo Owner { get; }
    public IPluginPanel Panel { get; }
    public string PanelId { get; }

    public int Order { get; }

    public string WindowId => $"{Owner.Id}/{PanelId}";

    public string LastTitle { get; internal set; }

    public Exception? Fault
    {
        get => _fault;
        internal set => _fault = value;
    }

    public bool IsVisible
    {
        get => _visible;
        set => _visible = value;
    }

    public bool IsRegistered => _registered;

    internal bool ShouldDraw => _registered && _visible && _fault is null;

    public void ClearFault() => _fault = null;

    public void Dispose()
    {
        lock (DrawGate)
        {
            if (!_registered) return;
            _registered = false;
        }
        _registry.Remove(this);
    }
}

internal sealed class PluginOverlayRegistry(PanelRegistry registry, PluginInfo owner) : IOverlayRegistry
{
    private readonly object _gate = new();
    private bool _closed;

    public bool IsAvailable => registry.IsAvailable && !Volatile.Read(ref _closed);

    public IPanelRegistration Register(IPluginPanel panel)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return registry.Register(owner, panel);
        }
    }

    public int Close()
    {
        lock (_gate)
        {
            if (_closed) return 0;
            _closed = true;
        }
        return registry.RemoveAll(owner.Id);
    }
}
