// using EventStore.Connectors.Contracts;
// using EventStore.Connectors.Control.Activation;
// using EventStore.Connectors.Control.Contracts;
// using EventStore.Connectors.Control.Contracts.Activation;
// using EventStore.Connectors.Control.Contracts.Coordination;
// using Google.Protobuf.WellKnownTypes;
// using Grpc.Core;
// using Status = EventStore.Connectors.Contracts.Status;
//
// namespace EventStore.Connectors.Control;
//
// public class ControlService : ControlPlaneApi.ControlPlaneApiBase {
//     public ControlService(ConnectorsActivator activator) {
//         Activator = activator;
//     }
//
//     ConnectorsActivator Activator { get; }
//
//     public override async Task<CommandResult> Activate(ActivateConnectors command, ServerCallContext context) {
//         var result = await Activator.Activate(command, context.CancellationToken);
//
//         return new CommandResult {
//             RequestId = null,
//             Status = new Status {
//                 // Code    = Code.Ok, // what a tire-fire...
//                 Message = "Connectors activated"
//             },
//             Payload = Any.Pack(result)
//         };
//     }
//
//     public override async Task<CommandResult> Deactivate(DeactivateConnectors command, ServerCallContext context) {
//         var result = await Activator.Deactivate(command, context.CancellationToken);
//
//         return new CommandResult {
//             RequestId = context.RequestHeaders.GetValue("request-id"),
//             Status = new Status {
//                 // Code    = Code.Ok, // what a tire-fire...
//                 Message = "Connectors deactivated"
//             },
//             Payload = Any.Pack(result)
//         };
//     }
//
//     public override Task<ConnectorsAssignment> CurrentAssignment(Empty request, ServerCallContext context) => base.CurrentAssignment(request, context);
//
//     public override Task<CommandResult> Rebalance(RebalanceConnectors request, ServerCallContext context) => base.Rebalance(request, context);
// }