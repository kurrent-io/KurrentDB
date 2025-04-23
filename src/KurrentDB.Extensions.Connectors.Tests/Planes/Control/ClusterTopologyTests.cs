using KurrentDB.Connectors.Planes.Control.Model;
using KurrentDB.Toolkit.Testing.Extensions;
using KurrentDB.Toolkit.Testing.Fixtures;
using KurrentDB.Toolkit.Testing.Xunit;

namespace KurrentDB.Connectors.Tests.Planes.Control;

[Trait("Category", "ControlPlane")]
public class ClusterTopologyTests(ITestOutputHelper output, FastFixture fixture) : FastTests(output, fixture) {
	[Fact]
	public void cluster_members_are_always_in_order() {
		List<global::KurrentDB.Connectors.Planes.Control.Model.ClusterNode> ordered = [
			new(Guid.NewGuid(), ClusterNodeState.Unmapped),
			new(Guid.NewGuid(), ClusterNodeState.Unmapped),
			new(Guid.NewGuid(), ClusterNodeState.Leader),
			new(Guid.NewGuid(), ClusterNodeState.Leader),
			new(Guid.NewGuid(), ClusterNodeState.Follower),
			new(Guid.NewGuid(), ClusterNodeState.Follower),
			new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica),
			new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
		];

		var fromOrdered  = ClusterTopology.From(ordered);
		var fromReversed = ClusterTopology.From(ordered.With(x => x.Reverse()));
		var fromRandom   = ClusterTopology.From(Fixture.Faker.PickRandom(ordered, ordered.Count));

		fromOrdered.Should()
			.BeEquivalentTo(fromReversed).And
			.BeEquivalentTo(fromRandom);
	}

	class FallbackTestCases : TestCaseGenerator<FallbackTestCases> {
		protected override IEnumerable<object[]> Data() {
			yield return [
				ClusterNodeState.ReadOnlyReplica,
				ClusterNodeState.ReadOnlyReplica,
				new[] {
					ClusterNodeState.Leader,
					ClusterNodeState.Follower,
					ClusterNodeState.ReadOnlyReplica
				}
			];

			yield return [
				ClusterNodeState.Follower,
				ClusterNodeState.Follower,
				new[] {
					ClusterNodeState.Leader,
					ClusterNodeState.Follower,
					ClusterNodeState.ReadOnlyReplica
				}
			];

			yield return [
				ClusterNodeState.Leader,
				ClusterNodeState.Leader,
				new[] {
					ClusterNodeState.Leader,
					ClusterNodeState.Follower,
					ClusterNodeState.ReadOnlyReplica
				}
			];

			yield return [
				ClusterNodeState.ReadOnlyReplica,
				ClusterNodeState.Follower,
				new[] {
					ClusterNodeState.Leader,
					ClusterNodeState.Follower
				}
			];

			yield return [
				ClusterNodeState.Follower,
				ClusterNodeState.Leader,
				new[] {
					ClusterNodeState.Leader
				}
			];

			yield return [
				ClusterNodeState.Unmapped,
				ClusterNodeState.ReadOnlyReplica,
				new[] {
					ClusterNodeState.Leader,
					ClusterNodeState.Follower,
					ClusterNodeState.ReadOnlyReplica
				}
			];

			yield return [
				ClusterNodeState.Unmapped,
				ClusterNodeState.Follower,
				new[] {
					ClusterNodeState.Leader,
					ClusterNodeState.Follower
				}
			];

			yield return [
				ClusterNodeState.Unmapped,
				ClusterNodeState.Leader,
				new[] {
					ClusterNodeState.Leader
				}
			];
		}
	}

	[Theory, FallbackTestCases]
	public void cluster_members_falls_back_when_affinity_not_matched(ClusterNodeState affinity, ClusterNodeState expected, ClusterNodeState[] activeStates) {
		var ordered  = activeStates.Select(x => new global::KurrentDB.Connectors.Planes.Control.Model.ClusterNode(Guid.NewGuid(), x));
		var topology = ClusterTopology.From(ordered);

		var result = topology.GetNodesByAffinity(affinity);

        result.Should()
            .NotBeEmpty("because there should be at least one node")
            .And.Contain(x => x.State == expected, "because the nodes should fall back to the next higher state");

		result.All(x => x.State == expected).Should().BeTrue("because all nodes should be of the same state");
	}
}