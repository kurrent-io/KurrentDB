using EventStore.Testing.Xunit.Extensions.AssemblyFixture;

namespace EventStore.Testing.Fixtures;

/// <summary>
/// Manages the lifetime of the SharedEventStoreTestNode static instance.
/// Must be registered as an AssemblyFixture using the <see cref="AssemblyFixtureAttribute"/> in the test assembly that requires a shared instance.
/// </summary>
public class SharedEventStoreTestNodeLifetime : IAsyncLifetime {
    public async Task InitializeAsync() => await SharedEventStoreTestNode.Instance.StartInner();
    public async Task DisposeAsync()    => await SharedEventStoreTestNode.Instance.DisposeAsyncInner();
}

public class SharedEventStoreTestNode : EventStoreTestNode {
    public static SharedEventStoreTestNode Instance { get; } = new();

    // Only to be called by the SharedEventStoreTestNodeLifetime.
    internal Task      StartInner()        => base.Start();
    internal ValueTask DisposeAsyncInner() => base.DisposeAsync();

    // Prevent accidental instantiation of the shared EventStore instance (noop).
    public override Task      Start()   => Task.CompletedTask;
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Protect against accidental stoppage/restarts of the shared EventStore instance which would affect tests executing concurrently.
    public override Task Restart(TimeSpan delay) => throw new InvalidOperationException("Restart is not supported for shared EventStoreTestNode instances.");
    public override Task Stop()                  => throw new InvalidOperationException("Stop is not supported for shared EventStoreTestNode instances.");
}