// using EventStore.Connect.Connectors;
// using EventStore.Connectors.Control.Activation;
// using EventStore.Connectors.Control.Coordination;
// using Google.Protobuf.Collections;
// using Google.Protobuf.WellKnownTypes;
//
// namespace EventStore.Connectors.Control;
//
// static class ControlPlaneContractsMapping {
//     public static Contracts.Connector MapToConnector(this RegisteredConnector source) =>
//         new() {
//             ConnectorId = source.ConnectorId,
//             Revision    = source.Revision,
//             Settings    = { source.Settings }
//         };
//
//     public static Contracts.Connector MapToConnector(this ConnectorsTaskManager.ConnectorProcessInfo source) =>
//         new() {
//             ConnectorId = source.ConnectorId,
//             Revision    = source.Revision
//         };
//
//     public static Contracts.Connector[] MapToConnectors(this IEnumerable<ConnectorsTaskManager.ConnectorProcessInfo> source) =>
//         source.Select(MapToConnector).ToArray();
//
//     public static Timestamp MapToUtcNowTimestamp(this TimeProvider source) =>
//         source.GetUtcNow().ToTimestamp();
//
//     public static ConnectorId[] ToConnectorIds(this RepeatedField<Contracts.Connector> source) =>
//         source.Select(x => ConnectorId.From(x.ConnectorId)).ToArray();
// }