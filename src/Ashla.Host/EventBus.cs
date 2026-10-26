using Ashla.SDK;

namespace Ashla.Host;

internal sealed class EventBus(ILogger log) : IEventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Subscription>> _handlers = [];

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        Subscription subscription = null!;
        subscription = new Subscription(handler, () =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var list))
                {
                    list.Remove(subscription);
                    if (list.Count == 0) _handlers.Remove(typeof(TEvent));
                }
            }
        });

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                _handlers[typeof(TEvent)] = list = [];
            list.Add(subscription);
        }
        return subscription;
    }

    public void Publish<TEvent>(TEvent value) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(value);
        Subscription[] subscribers;
        lock (_gate)
            subscribers = _handlers.TryGetValue(typeof(TEvent), out var list)
                ? [.. list]
                : [];

        foreach (var subscriber in subscribers)
        {
            if (!subscriber.IsActive) continue;
            try { ((Action<TEvent>)subscriber.Handler)(value); }
            catch (Exception ex) { log.Error($"Subscriber failed handling {typeof(TEvent).Name}", ex); }
        }
    }

    private sealed class Subscription(Delegate handler, Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public Delegate Handler { get; } = handler;
        public bool IsActive => Volatile.Read(ref _unsubscribe) is not null;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}

internal sealed class ScopedEventBus(IEventBus inner) : IEventBus
{
    private readonly object _gate = new();
    private readonly HashSet<TrackedSubscription> _subscriptions = [];
    private bool _closed;

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var tracked = new TrackedSubscription(this, inner.Subscribe(handler));
            _subscriptions.Add(tracked);
            return tracked;
        }
    }

    public void Publish<TEvent>(TEvent value) where TEvent : class
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed), this);
        inner.Publish(value);
    }

    public int Close()
    {
        TrackedSubscription[] leftovers;
        lock (_gate)
        {
            if (_closed) return 0;
            _closed = true;
            leftovers = [.. _subscriptions];
            _subscriptions.Clear();
        }
        foreach (var subscription in leftovers) subscription.DisposeInner();
        return leftovers.Length;
    }

    private void Forget(TrackedSubscription subscription)
    {
        lock (_gate) _subscriptions.Remove(subscription);
    }

    private sealed class TrackedSubscription(ScopedEventBus owner, IDisposable inner) : IDisposable
    {
        private IDisposable? _inner = inner;

        public void Dispose()
        {
            owner.Forget(this);
            DisposeInner();
        }

        public void DisposeInner() => Interlocked.Exchange(ref _inner, null)?.Dispose();
    }
}
