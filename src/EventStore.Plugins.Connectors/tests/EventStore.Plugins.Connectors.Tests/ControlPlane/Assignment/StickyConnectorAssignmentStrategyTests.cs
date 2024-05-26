using EventStore.Connectors.ControlPlane;
using EventStore.Connectors.ControlPlane.Assignment;
using EventStore.Streaming;
using EventStore.Testing.Fixtures;
using MassTransit;

namespace EventStore.Plugins.Connectors.Tests.ControlPlane.Assignment;

[Trait("Category", "Assignment")]
public class StickyConnectorAssignmentStrategyTests(ITestOutputHelper output, FastFixture fixture) : FastTests(output, fixture) {
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
            { topology[0].NodeId, NodeConnectorsAssignment.From([connectors[0].ConnectorId]) },
            { topology[1].NodeId, NodeConnectorsAssignment.From([connectors[1].ConnectorId]) },
            { topology[2].NodeId, NodeConnectorsAssignment.From([connectors[2].ConnectorId, connectors[3].ConnectorId]) }
        });
        
        var result = new LeastLoadedWithAffinityConnectorAssignor().Assign(topology, connectors);
		
		result.Should().BeEquivalentTo(expectedResult);
	}
	
	[Fact]
	public void assigns_to_single_leader() {
		var topology  = ClusterTopology.From(
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
		
		var expectedResult = new Dictionary<ClusterNodeId, NodeConnectorsAssignment> {
			{ topology[1].NodeId, NodeConnectorsAssignment.From([connectors[0].ConnectorId, connectors[1].ConnectorId, connectors[2].ConnectorId]) },
		};
		
		var result = new StickyWithAffinityConnectorAssignor().Assign(topology, connectors);
		
		result.Should().BeEquivalentTo(expectedResult);
	}
	
	[Theory]
	[InlineData(ClusterNodeState.Leader, 3, 1, 9)]
	[InlineData(ClusterNodeState.Follower, 3, 3, 9)]
	public void assigns_with_affinity(ClusterNodeState affinity, int numberOfNodes, int numberOfNodesWithAffinity, int numberOfConnectors) {
		var topology = ClusterTopology.From(
			Enumerable.Range(1, numberOfNodes - numberOfNodesWithAffinity)
				.Select(_ => new EventStore.Connectors.ControlPlane.ClusterNode(NewId.Next().ToGuid(), ClusterNodeState.Follower)).Concat(
					Enumerable.Range(1, numberOfNodesWithAffinity)
						.Select(_ => new EventStore.Connectors.ControlPlane.ClusterNode(NewId.Next().ToGuid(), affinity))
				)
		);
		
		var connectors = Enumerable.Range(1, numberOfConnectors)
			.Select(_ => new ConnectorResource(NewId.Next().ToGuid(), affinity))
			.ToArray();
	
		var indexes = connectors
			.Where(connector => connector.Affinity == affinity)
			.Select(connector => (NodeIndex: (int)(HashGenerators.MurmurHash3(connector.ConnectorId) % numberOfNodesWithAffinity), Connector: connector))
			.ToArray();
		
		var expectedResult = indexes
			.GroupBy(x => x.NodeIndex)
			.ToDictionary(
				x => topology.NodesByState[affinity][x.Key].NodeId, 
				x => NodeConnectorsAssignment.From(x.Select(y => y.Connector.ConnectorId).ToArray())
			);
		
        var result = new StickyWithAffinityConnectorAssignor().Assign(topology, connectors);
		
		result.Should().BeEquivalentTo(expectedResult, "because the connectors should be assigned to the nodes with the same affinity");
	}
}