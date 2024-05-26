using EventStore.Core;
using EventStore.Core.Bus;

namespace EventStore.Testing.Fixtures;

public class ClusterVNodeFixture : FastFixture {
	static ClusterVNodeFixture() => ClusterVNodeActivator.Initialize(); // fake trigger

	public IPublisher          Publisher   => ClusterVNodeActivator.Publisher;
	public ISubscriber         Subscriber  => ClusterVNodeActivator.Subscriber;
	public ClusterVNodeOptions NodeOptions => ClusterVNodeActivator.NodeOptions;
}

public abstract class ClusterVNodeTests<TFixture> : IClassFixture<TFixture> where TFixture : ClusterVNodeFixture {
	protected ClusterVNodeTests(ITestOutputHelper output, TFixture fixture) =>
		Fixture = fixture.With(x => x.CaptureTestRun(output));

	protected TFixture Fixture { get; }

	protected IPublisher          Publisher   => Fixture.Publisher;
	protected ISubscriber         Subscriber  => Fixture.Subscriber;
	protected ClusterVNodeOptions NodeOptions => Fixture.NodeOptions;
}

public abstract class ClusterVNodeTests(ITestOutputHelper output, ClusterVNodeFixture fixture) 
	: ClusterVNodeTests<ClusterVNodeFixture>(output, fixture);
