using KurrentDB.Connectors.Planes.Control.Assignment;
using KurrentDB.Connectors.Planes.Control.Assignment.Assignors;
using KurrentDB.Connectors.Planes.Control.Model;
using KurrentDB.Toolkit.Testing.Fixtures;

namespace KurrentDB.Connectors.Tests.Planes.Control.Assignment;

[Trait("Category", "Assignment")]
[Trait("Category", "ControlPlane")]
public class RoundRobinConnectorAssignmentStrategyTests(ITestOutputHelper output, FastFixture fixture) : FastTests(output, fixture) {
	[Fact]
	public void assigns_directly() {
        var topology = ClusterTopology.From(
            new(Guid.NewGuid(), ClusterNodeState.Leader),
            new(Guid.NewGuid(), ClusterNodeState.Follower),
            new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica)
        );

        ConnectorResource[] connectors = [
            new(Guid.NewGuid(), ClusterNodeState.Leader),
            new(Guid.NewGuid(), ClusterNodeState.Follower),
            new(Guid.NewGuid(), ClusterNodeState.ReadOnlyReplica),
            new(Guid.NewGuid(), ClusterNodeState.Unmapped)
        ];

        var expectedResult = new ClusterConnectorsAssignment(Guid.NewGuid(), new() {
            { topology[0], NodeConnectorsAssignment.From([connectors[0]]) },
            { topology[1], NodeConnectorsAssignment.From([connectors[1]]) },
            { topology[2], NodeConnectorsAssignment.From([connectors[2], connectors[3]]) }
        });

        var result = new LeastLoadedWithAffinityConnectorAssignor().Assign(topology, connectors);

        result.Should().BeEquivalentTo(expectedResult);
	}
}