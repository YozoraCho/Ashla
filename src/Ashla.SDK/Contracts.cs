namespace Ashla.SDK;

public static class AshlaApi
{
    public const int Level = 1;
}

public interface IAshlaPlugin : IDisposable
{
    string Name { get; }
    void Initialize(IPluginContext context);
}

public interface IPluginContext
{
    PluginInfo Plugin { get; }
    ILogger Log { get; }
    IEventBus Events { get; }
    IGameState GameState { get; }
    IOverlayRegistry Overlays { get; }

    IPluginConfig Config { get; }

    IToasts Toasts { get; }

    IEncounterArchive Encounters { get; }

    IAshlaCapabilities Capabilities { get; }
}

public interface IAshlaCapabilities
{
    bool Has<T>() where T : class;

    T? Get<T>() where T : class;

    IReadOnlyCollection<Type> Available { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresCapabilityAttribute(Type capability) : Attribute
{
    public Type Capability { get; } = capability;
}

public sealed record SavedEncounter(
    string Id,
    string Title,
    string? Area,
    string Character,
    string? Discipline,
    DateTime Start,
    TimeSpan Duration,
    double Damage,
    double Healing,
    double DamageTaken);

public interface IEncounterArchive
{
    bool IsAvailable { get; }

    IReadOnlyList<SavedEncounter> List();

    IReadOnlyList<CombatEvent> Load(string id);

    event Action? Changed;
}

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

public interface IToasts
{
    void Show(string title, string? text = null, ToastKind kind = ToastKind.Info);
}

public interface IPluginConfig
{
    string FilePath { get; }

    T Load<T>() where T : class, new();

    bool Save<T>(T settings) where T : class;
}

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public interface IEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    void Publish<TEvent>(TEvent value) where TEvent : class;
}

[Flags]
public enum GameCapabilities
{
    None = 0,
    CombatLog = 1 << 0,
    LocalPlayer = 1 << 1,
    CurrentTarget = 1 << 2,
    Overlay = 1 << 3,
    NativeUi = 1 << 4
}

public interface IGameState
{
    GameCapabilities Capabilities { get; }
    bool TryGetLocalPlayer(out PlayerSnapshot? player);
    bool TryGetCurrentTarget(out TargetSnapshot? target);
}

public sealed record PlayerSnapshot(string Name, int? Level);
public sealed record TargetSnapshot(string Name);

public sealed record CombatLogLine(DateTimeOffset ObservedAt, string SourceFile, string RawText);

public sealed record PluginInfo(string Id, string Name, string Version, string? Author, string? Description);

public interface IOverlayRegistry
{
    bool IsAvailable { get; }

    IPanelRegistration Register(IPluginPanel panel);
}

public interface IPanelRegistration : IDisposable
{
    string PanelId { get; }

    bool IsVisible { get; set; }

    bool IsRegistered { get; }
}

public interface IPluginPanel
{
    string Id { get; }

    string Title { get; }

    void Draw();
}

public interface IPluginPanelAppearance
{
    PanelAppearance Appearance { get; }
}

public sealed record PanelAppearance
{
    public bool ShowTitleBar { get; init; } = true;

    public float? BackgroundOpacity { get; init; }

    public float? Padding { get; init; }

    public float? DefaultWidth { get; init; }

    public float? DefaultHeight { get; init; }

    public bool NoScrollbar { get; init; }

    public bool FitToContent { get; init; }

    public bool ClickThrough { get; init; }
}
