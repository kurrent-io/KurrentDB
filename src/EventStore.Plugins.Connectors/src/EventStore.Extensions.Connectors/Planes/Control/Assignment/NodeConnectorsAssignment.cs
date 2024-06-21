using System.Collections;
using EventStore.Connect.Connectors;

namespace EventStore.Connectors.Control.Assignment;

[PublicAPI]
public record NodeConnectorsAssignment : IReadOnlyCollection<ConnectorId> {
    NodeConnectorsAssignment(IEnumerable<ConnectorId> unchanged, IEnumerable<ConnectorId> assigned, IEnumerable<ConnectorId> revoked) {
        Unchanged = unchanged.ToArray();
        Assigned  = assigned.ToArray();
        Revoked   = revoked.ToArray();

        Connectors = Unchanged.Concat(Assigned).ToArray();
    }

    public NodeConnectorsAssignment(IEnumerable<ConnectorId> assigned) {
        Connectors = Assigned = assigned.ToArray();
        Unchanged  = [];
        Revoked    = [];
    }

    IReadOnlyList<ConnectorId> Connectors { get; }

    /// <summary>
    /// Connectors that are already assigned to the node.
    /// </summary>
    public ConnectorId[] Unchanged { get; }

    /// <summary>
    /// Connectors that are newly assigned to the node.
    /// </summary>
    public ConnectorId[] Assigned { get; }

    /// <summary>
    /// Connectors that are revoked from the node.
    /// </summary>
    public ConnectorId[] Revoked { get; }

    /// <summary>
    /// Applies the new assignment to the current assignment.
    /// </summary>
    /// <param name="newNodeAssignment">An enumerable of <see cref="ConnectorId"/> that are newly assigned.</param>
    /// <returns>
    /// A new instance of <see cref="NodeConnectorsAssignment"/> with the updated assignments.
    /// The returned object contains connectors that are unchanged, newly assigned, and revoked.
    /// </returns>
    public NodeConnectorsAssignment Apply(IReadOnlyCollection<ConnectorId> newNodeAssignment) =>
        new(
            unchanged: newNodeAssignment.Intersect(Connectors),
            assigned: newNodeAssignment.Except(Connectors),
            revoked: Connectors.Except(newNodeAssignment)
        );

    public IEnumerator<ConnectorId> GetEnumerator() => Connectors.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => Connectors.Count;

    public static NodeConnectorsAssignment From(IEnumerable<ConnectorId> assigned) => new(assigned);
}