using Ashla.Host;

namespace Ashla.Tests;

public class EventBusTests
{
    private sealed record Ping(int Value);

    private readonly TestLogger _log = new();

    [Fact]
    public void Publish_ReachesSubscribers_UntilDisposed()
    {
        var bus = new EventBus(_log);
        var seen = new List<int>();
        var subscription = bus.Subscribe<Ping>(p => seen.Add(p.Value));

        bus.Publish(new Ping(1));
        subscription.Dispose();
        bus.Publish(new Ping(2));

        Assert.Equal([1], seen);
    }

    [Fact]
    public void SubscriptionDisposedDuringPublish_IsNotCalledAfterwards()
    {
        var bus = new EventBus(_log);
        IDisposable? second = null;
        int secondCalls = 0;
        bus.Subscribe<Ping>(_ => second!.Dispose());
        second = bus.Subscribe<Ping>(_ => secondCalls++);

        bus.Publish(new Ping(1));

        Assert.Equal(0, secondCalls);
    }

    [Fact]
    public void ThrowingHandler_IsIsolated()
    {
        var bus = new EventBus(_log);
        int calls = 0;
        bus.Subscribe<Ping>(_ => throw new InvalidOperationException());
        bus.Subscribe<Ping>(_ => calls++);

        bus.Publish(new Ping(1));

        Assert.Equal(1, calls);
        Assert.Single(_log.Errors);
    }

    [Fact]
    public void ScopedBus_Close_DisposesLeftoverSubscriptions()
    {
        var bus = new EventBus(_log);
        var scoped = new ScopedEventBus(bus);
        int calls = 0;
        scoped.Subscribe<Ping>(_ => calls++);
        scoped.Subscribe<Ping>(_ => calls++).Dispose();

        bus.Publish(new Ping(1));
        Assert.Equal(1, scoped.Close());
        bus.Publish(new Ping(2));

        Assert.Equal(1, calls);
        Assert.Throws<ObjectDisposedException>(() => scoped.Subscribe<Ping>(_ => { }));
        Assert.Throws<ObjectDisposedException>(() => scoped.Publish(new Ping(3)));
        Assert.Equal(0, scoped.Close());
    }

    [Fact]
    public void ScopedBus_PublishesToWholeBus()
    {
        var bus = new EventBus(_log);
        var a = new ScopedEventBus(bus);
        var b = new ScopedEventBus(bus);
        int received = 0;
        b.Subscribe<Ping>(_ => received++);

        a.Publish(new Ping(1));

        Assert.Equal(1, received);
    }
}
