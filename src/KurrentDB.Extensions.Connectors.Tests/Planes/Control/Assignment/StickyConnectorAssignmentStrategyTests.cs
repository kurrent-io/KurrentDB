using Kurrent.Surge;
using KurrentDB.Connectors.Planes.Control.Assignment;
using KurrentDB.Connectors.Planes.Control.Assignment.Assignors;
using KurrentDB.Connectors.Planes.Control.Model;
using KurrentDB.Toolkit.Testing.Fixtures;
using MassTransit;

namespace KurrentDB.Connectors.Tests.Planes.Control.Assignment;

[Trait("Category", "Assignment")]
[Trait("Category", "ControlPlane")]
public class StickyConnectorAssignmentStrategyTests(ITestOutputHelper output, FastFixture fixture)
    : FastTests(output, fixture) {
    [Fact]
    public void assigns_directly() {
        var topology = ClusterTopology.From(
            new(NewId.Next().ToGuid(), ClusterNodeState.Leader),
            new(NewId.Next().ToGuid(), ClusterNodeState.Follower),
            new(NewId.Next().ToGuid(), ClusterNodeState.ReadOnlyReplica)
        );

        ConnectorResource[] connectors = [
            new(NewId.Next().ToGuid(), ClusterNodeState.Leader), new(NewId.Next().ToGuid(), ClusterNodeState.Follower),
            new(NewId.Next().ToGuid(), ClusterNodeState.ReadOnlyReplica),
            new(NewId.Next().ToGuid(), ClusterNodeState.Unmapped)
        ];

        var expectedResult = new ClusterConnectorsAssignment(
            Guid.NewGuid(),
            new() {
                { topology[0].NodeId, NodeConnectorsAssignment.From([connectors[0].ConnectorId]) },
                { topology[1].NodeId, NodeConnectorsAssignment.From([connectors[1].ConnectorId]) }, {
                    topology[2].NodeId,
                    NodeConnectorsAssignment.From([connectors[2].ConnectorId, connectors[3].ConnectorId])
                }
            }
        );

        var result = new LeastLoadedWithAffinityConnectorAssignor().Assign(topology, connectors);

        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public void assigns_to_single_leader() {
        var topology = ClusterTopology.From(
            new(NewId.Next().ToGuid(), ClusterNodeState.Unmapped),
            new(NewId.Next().ToGuid(), ClusterNodeState.Leader),
            new(NewId.Next().ToGuid(), ClusterNodeState.Follower),
            new(NewId.Next().ToGuid(), ClusterNodeState.ReadOnlyReplica)
        );

        var connectors = new[] {
            new ConnectorResource(NewId.Next().ToGuid(), ClusterNodeState.Leader),
            new ConnectorResource(NewId.Next().ToGuid(), ClusterNodeState.Leader),
            new ConnectorResource(NewId.Next().ToGuid(), ClusterNodeState.Leader)
        };

        var expectedResult = new ClusterConnectorsAssignment(
            Guid.NewGuid(),
            new() {
                { topology[0], new([]) },
                { topology[1], NodeConnectorsAssignment.From([connectors[0], connectors[1], connectors[2]]) },
                { topology[2], new([]) },
                { topology[3], new([]) },
            }
        );

        var result = new StickyWithAffinityConnectorAssignor().Assign(topology, connectors);

        result.Should().BeEquivalentTo(expectedResult);
    }

    [Theory(Skip = "Need to be refactored after final mulinode hosting changes")]
    [InlineData(ClusterNodeState.Leader, 3, 1, 9)]
    [InlineData(ClusterNodeState.Follower, 3, 3, 9)]
    public void assigns_with_affinity(
        ClusterNodeState affinity, int numberOfNodes, int numberOfNodesWithAffinity, int numberOfConnectors
    ) {
        var nodesThatDoNotMatchAffinity = Enumerable.Range(1, numberOfNodes - numberOfNodesWithAffinity)
            .Select(
                _ => new global::KurrentDB.Connectors.Planes.Control.Model.ClusterNode(
                    NewId.Next().ToGuid(),
                    ClusterNodeState.Follower
                )
            )
            .ToArray();

        var topology = ClusterTopology.From(
            nodesThatDoNotMatchAffinity.Concat(
                Enumerable.Range(1, numberOfNodesWithAffinity)
                    .Select(_ => new global::KurrentDB.Connectors.Planes.Control.Model.ClusterNode(NewId.Next().ToGuid(), affinity))
                    .ToArray()
            )
        );

        var connectors = Enumerable.Range(1, numberOfConnectors)
            .Select(_ => new ConnectorResource(NewId.Next().ToGuid(), affinity))
            .ToArray();

        var expectedNodeAssignments = connectors
            .Where(connector => connector.Affinity == affinity)
            .Select(
                connector => (
                    NodeIndex: (int)(HashGenerators.FromString.MurmurHash3(connector.ConnectorId) % numberOfNodesWithAffinity),
                    Connector: connector)
            )
            .GroupBy(x => x.NodeIndex)
            .ToDictionary(
                x => topology.NodesByState[affinity][x.Key].NodeId,
                x => NodeConnectorsAssignment.From(x.Select(y => y.Connector.ConnectorId).ToArray())
            )
            .ToArray();

        var expectedResult = expectedNodeAssignments.Concat(
            nodesThatDoNotMatchAffinity.Select(
                nd => new KeyValuePair<ClusterNodeId, NodeConnectorsAssignment>(
                    nd.NodeId,
                    NodeConnectorsAssignment.From([])
                )
            )
        );

        var result = new StickyWithAffinityConnectorAssignor().Assign(topology, connectors);

        result.Should().BeEquivalentTo(
            expectedResult,
            "because the connectors should be assigned to the nodes with the same affinity"
        );
    }
}