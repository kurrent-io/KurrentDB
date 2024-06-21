using EventStore.Connectors.Control.Assignment.Assignors;

namespace EventStore.Connectors.Control.Assignment;

public delegate IConnectorAssignor GetConnectorAssignor(ConnectorAssignmentStrategy strategy = ConnectorAssignmentStrategy.Unspecified);

[PublicAPI]
public class ConnectorAssignorProvider {
    public ConnectorAssignorProvider(IEnumerable<IConnectorAssignor> assignors) =>
        Assignors = assignors.ToDictionary(x => x.Type);

    public ConnectorAssignorProvider() : this([
        new RoundRobinWithAffinityConnectorAssignor(),
        new LeastLoadedWithAffinityConnectorAssignor(),
        new StickyWithAffinityConnectorAssignor()
    ]) { }

    Dictionary<ConnectorAssignmentStrategy, IConnectorAssignor> Assignors { get; }

    public IConnectorAssignor Get(ConnectorAssignmentStrategy strategy) {
        if (!Assignors.TryGetValue(
                strategy == ConnectorAssignmentStrategy.Unspecified
                    ? ConnectorAssignmentStrategy.StickyWithAffinity
                    : strategy, out var assignmentStrategy
        )) 
            throw new NotImplementedException($"{strategy} assignor not registered");

        return assignmentStrategy;
    }
}