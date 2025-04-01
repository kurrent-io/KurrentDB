using EventStore.Connectors.Control;
using EventStore.Connectors.Control.Assignment;
using EventStore.Connectors.Control.Assignment.Assignors;
using EventStore.Toolkit.Testing.Fixtures;
using EventStore.Toolkit.Testing.Xunit;
using MassTransit;

namespace EventStore.Extensions.Connectors.Tests.Control.Assignment;

[Trait("Category", "Assignment")]
[Trait("Category", "ControlPlane")]
public class LeastLoadedWithAffinityConnectorAssignorTests(ITestOutputHelper output, FastFixture fixture) : FastTests(output, fixture) {
	[Fact]
	public void assigns_directly() {
		var topology  = ClusterTopology.From(
			new(NewId.Next().ToGuid(), ClusterNodeState.Leader),
			new(NewId.Next().ToGuid(), ClusterNodeState.Follower),
			new(NewId.Next().ToGuid(), ClusterNodeState.ReadOnlyReplica)
		);

        ConnectorResource[] connectors = [
            new(NewId.Next().ToGuid(), ClusterNodeState.Leader),
			new(NewId.Next().ToGuid(), ClusterNodeState.Follower),
			new(NewId.Next().ToGuid(), ClusterNodeState.ReadOnlyReplica),
			new(NewId.Next().ToGuid(), ClusterNodeState.Unmapped)
        ];

		var expectedResult = new ClusterConnectorsAssignment(Guid.NewGuid(), new() {
			{ topology[0], NodeConnectorsAssignment.From([connectors[0]]) },
			{ topology[1], NodeConnectorsAssignment.From([connectors[1]]) },
			{ topology[2], NodeConnectorsAssignment.From([connectors[2], connectors[3]]) }
		});

		var result = new LeastLoadedWithAffinityConnectorAssignor().Assign(topology, connectors);

		result.Should().BeEquivalentTo(
            expectedResult, x => x.Excluding(f => f.AssignmentId)
        );
	}

	[Fact]
	public void assigns_to_single_leader() {
        var topology = ClusterTopology.From(
            new(NewId.Next().ToGuid(), ClusterNodeState.Unmapped),
            new(NewId.Next().ToGuid(), ClusterNodeState.Leader),
            new(NewId.Next().ToGuid(), ClusterNodeState.Follower),
            new(NewId.Next().ToGuid(), ClusterNodeState.ReadOnlyReplica)
        );

        ConnectorResource[] connectors = [
            new(NewId.Next().ToGuid(), ClusterNodeState.Leader),
			new(NewId.Next().ToGuid(), ClusterNodeState.Leader),
			new(NewId.Next().ToGuid(), ClusterNodeState.Leader)
        ];

        var expectedResult = new ClusterConnectorsAssignment(Guid.NewGuid(), new() {
            { topology[0], new([]) },
            { topology[1], NodeConnectorsAssignment.From([connectors[0], connectors[1], connectors[2]]) },
            { topology[2], new([]) },
            { topology[3], new([]) },
        });

        var result = new LeastLoadedWithAffinityConnectorAssignor().Assign(topology, connectors);

        result.Should().BeEquivalentTo(
            expectedResult, x => x.Excluding(f => f.AssignmentId)
        );
	}

    public class AssignsWithAffinityTestCases : TestCaseGenerator<AssignsWithAffinityTestCases> {

        public record Case(ClusterNodeState ExpectedAffinity, ClusterTopology Topology, int NumberOfConnectors);

        protected override IEnumerable<object[]> Data() {
            // cluster with 3 nodes, 1 Leader with affinity
            yield return [
                new Case(ClusterNodeState.Leader,
                ClusterTopology.From(
                    new(Guid.NewGuid(), ClusterNodeState.Leader),
                    new(Guid.NewGuid(), ClusterNodeState.Follower),
                    new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
                ),
                1)
            ];

            // // cluster with 3 nodes, 1 Follower with affinity
            // yield return [
            //     ClusterNodeState.Follower,
            //     ClusterTopology.From(
            //         new(Guid.NewGuid(), ClusterNodeState.Leader),
            //         new(Guid.NewGuid(), ClusterNodeState.Follower),
            //         new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
            //     ),
            //     1
            // ];
            //
            // // cluster with 3 nodes, 1 ReadOnlyReplica with affinity
            // yield return [
            //     ClusterNodeState.ReadOnlyReplica,
            //     ClusterTopology.From(
            //         new(Guid.NewGuid(), ClusterNodeState.Leader),
            //         new(Guid.NewGuid(), ClusterNodeState.Follower),
            //         new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
            //     ),
            //     1
            // ];
            //
            //
            // // cluster with 3 nodes, 1 Follower with affinity
            // yield return [
            //     ClusterNodeState.Follower,
            //     ClusterTopology.From(
            //         new(Guid.NewGuid(), ClusterNodeState.Leader),
            //         new(Guid.NewGuid(), ClusterNodeState.Follower),
            //         new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
            //     ),
            //     2
            // ];
            //
            // // cluster with 3 nodes, 1 ReadOnlyReplica with affinity
            // yield return [
            //     ClusterNodeState.ReadOnlyReplica,
            //     ClusterTopology.From(
            //         new(Guid.NewGuid(), ClusterNodeState.Leader),
            //         new(Guid.NewGuid(), ClusterNodeState.Follower),
            //         new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
            //     ),
            //     2
            // ];
            //
            // // cluster with 3 nodes, 2 Followers with affinity
            // yield return [
            //     ClusterNodeState.Follower,
            //     ClusterTopology.From(
            //         new(Guid.NewGuid(), ClusterNodeState.Leader),
            //         new(Guid.NewGuid(), ClusterNodeState.Follower),
            //         new(Guid.NewGuid(), ClusterNodeState.Follower)
            //     ),
            //     4
            // ];
            //
            // // cluster with 4 nodes, 2 ReadOnlyReplicas with affinity
            // yield return [
            //     ClusterNodeState.ReadOnlyReplica,
            //     ClusterTopology.From(
            //         new(Guid.NewGuid(), ClusterNodeState.Leader),
            //         new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica),
            //         new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
            //     ),
            //     4
            // ];
        }
    }

    [Theory, AssignsWithAffinityTestCases]
	public void assigns_with_affinity(AssignsWithAffinityTestCases.Case testCase) {
		var connectors = Enumerable.Range(1, testCase.NumberOfConnectors)
            .Select(_ => new ConnectorResource(Guid.NewGuid(), testCase.ExpectedAffinity))
            .ToArray();

        ClusterNodeId[] nodesWithAffinity = testCase.Topology
            .NodesByState[testCase.ExpectedAffinity]
            .Select(x => x.NodeId)
            .ToArray();

        var numberOfConnectorsAssignedPerNode = testCase.NumberOfConnectors > 1
            ? testCase.NumberOfConnectors / nodesWithAffinity.Length
            : testCase.NumberOfConnectors;

        var result = new LeastLoadedWithAffinityConnectorAssignor().Assign(testCase.Topology, connectors);

        result.Count.Should().Be(testCase.Topology.Nodes.Count);
        result.Should().ContainKeys(nodesWithAffinity);

        foreach (var ass in result.Where(x => nodesWithAffinity.Contains(x.Key))) {
            ass.Value.Count.Should().Be(numberOfConnectorsAssignedPerNode);
        }
	}


 //     class AssignsWithAffinityTestCases : TestCaseGenerator<AssignsWithAffinityTestCases> {
 //
 //        public record Case(ClusterNodeState Affinity, ClusterTopology Topology, int NumberOfConnectors);
 //
 //        protected override IEnumerable<object[]> Data() {
 //            // cluster with 3 nodes, 1 Leader with affinity
 //            yield return [
 //                ClusterNodeState.Leader,
 //                ClusterTopology.From(
 //                    new(Guid.NewGuid(), ClusterNodeState.Leader),
 //                    new(Guid.NewGuid(), ClusterNodeState.Follower),
 //                    new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
 //                ),
 //                1
 //            ];
 //
 //            // cluster with 3 nodes, 1 Follower with affinity
 //            yield return [
 //                ClusterNodeState.Follower,
 //                ClusterTopology.From(
 //                    new(Guid.NewGuid(), ClusterNodeState.Leader),
 //                    new(Guid.NewGuid(), ClusterNodeState.Follower),
 //                    new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
 //                ),
 //                1
 //            ];
 //
 //            // cluster with 3 nodes, 1 ReadOnlyReplica with affinity
 //            yield return [
 //                ClusterNodeState.ReadOnlyReplica,
 //                ClusterTopology.From(
 //                    new(Guid.NewGuid(), ClusterNodeState.Leader),
 //                    new(Guid.NewGuid(), ClusterNodeState.Follower),
 //                    new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
 //                ),
 //                1
 //            ];
 //
 //
 //            // cluster with 3 nodes, 1 Follower with affinity
 //            yield return [
 //                ClusterNodeState.Follower,
 //                ClusterTopology.From(
 //                    new(Guid.NewGuid(), ClusterNodeState.Leader),
 //                    new(Guid.NewGuid(), ClusterNodeState.Follower),
 //                    new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
 //                ),
 //                2
 //            ];
 //
 //            // cluster with 3 nodes, 1 ReadOnlyReplica with affinity
 //            yield return [
 //                ClusterNodeState.ReadOnlyReplica,
 //                ClusterTopology.From(
 //                    new(Guid.NewGuid(), ClusterNodeState.Leader),
 //                    new(Guid.NewGuid(), ClusterNodeState.Follower),
 //                    new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
 //                ),
 //                2
 //            ];
 //
 //            // cluster with 3 nodes, 2 Followers with affinity
 //            yield return [
 //                ClusterNodeState.Follower,
 //                ClusterTopology.From(
 //                    new(Guid.NewGuid(), ClusterNodeState.Leader),
 //                    new(Guid.NewGuid(), ClusterNodeState.Follower),
 //                    new(Guid.NewGuid(), ClusterNodeState.Follower)
 //                ),
 //                4
 //            ];
 //
 //            // cluster with 4 nodes, 2 ReadOnlyReplicas with affinity
 //            yield return [
 //                ClusterNodeState.ReadOnlyReplica,
 //                ClusterTopology.From(
 //                    new(Guid.NewGuid(), ClusterNodeState.Leader),
 //                    new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica),
 //                    new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
 //                ),
 //                4
 //            ];
 //        }
 //    }
 //
 //    [Theory, AssignsWithAffinityTestCases]
	// public void assigns_with_affinity(ClusterNodeState expectedAffinity, ClusterTopology topology, int numberOfConnectors) {
	// 	var connectors = Enumerable.Range(1, numberOfConnectors)
 //            .Select(_ => new ConnectorResource(Guid.NewGuid(), expectedAffinity))
 //            .ToArray();
 //
 //        ClusterNodeId[] nodesWithAffinity = topology
 //            .NodesByState[expectedAffinity]
 //            .Select(x => x.NodeId)
 //            .ToArray();
 //
 //        var numberOfConnectorsAssignedPerNode = numberOfConnectors > 1
 //            ? numberOfConnectors / nodesWithAffinity.Length
 //            : numberOfConnectors;
 //
 //        var result = new LeastLoadedWithAffinityConnectorAssignor().Assign(topology, connectors);
 //
 //        result.Count.Should().Be(topology.Nodes.Count);
 //        result.Should().ContainKeys(nodesWithAffinity);
 //
 //        foreach (var ass in result.Where(x => nodesWithAffinity.Contains(x.Key))) {
 //            ass.Value.Count.Should().Be(numberOfConnectorsAssignedPerNode);
 //        }
	// }
}