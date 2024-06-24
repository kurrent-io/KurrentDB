using EventStore.Connectors.Contracts;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Plugins.Authorization;
using Eventuous;
using Grpc.Core;
using static EventStore.Connectors.Management.Contracts.Commands.ConnectorCommand.CommandOneofCase;
using Status = EventStore.Connectors.Contracts.Status;

//using StatusCode = global::Google.Rpc.Code;

namespace EventStore.Connectors.Management;

/// <summary>
///  Connector service implementation.
/// </summary>
public class ConnectorService(ConnectorApplication application, IAuthorizationProvider authorizationProvider) : ConnectorCommandService.ConnectorCommandServiceBase {
	ConnectorApplication Application { get; } = application;

	/// <summary>
	///  Executes a command on the connector.
	/// </summary>
	public override async Task<CommandResult> Execute(ConnectorCommand request, ServerCallContext context) {
        var user = context.GetHttpContext().User;
        await authorizationProvider.CheckAccessAsync(user, new Operation("connectors", "write"), context.CancellationToken);

        var execute = request.CommandCase switch {
            Create      => Application.Handle(request.Create, context.CancellationToken),
            Reconfigure => Application.Handle(request.Reconfigure, context.CancellationToken),
            Reset       => Application.Handle(request.Reset, context.CancellationToken),
            Delete      => Application.Handle(request.Delete, context.CancellationToken),
            Rename      => Application.Handle(request.Rename, context.CancellationToken),

			Start => Application.Handle(request.Start, context.CancellationToken),
			Stop  => Application.Handle(request.Stop, context.CancellationToken),

			RecordStateChange => Application.Handle(request.RecordStateChange, context.CancellationToken),
			RecordPositions   => Application.Handle(request.RecordPositions, context.CancellationToken),

			_ => throw new ArgumentOutOfRangeException(nameof(request.CommandCase))
		};

        try {
        	var result = await execute;

            return new CommandResult {
                RequestId = request.RequestId, Status = new Status {
                    // Code    = Google.Rpc.Code.Ok, // ambiguos reference ffs....
                    Message = null
                }

                //
                //  Status = new Status {
                //  	//Code    = Code.Ok,
                //  	Message = null
                //  },
                // Events = { ConvertChangesToEvents(result.Changes).ToArray() }
            };
        }
        catch (DomainException dex) {
        	// return a failed result
        	return new CommandResult {
        		RequestId = request.RequestId,
        		Status = new Status {
        			// Code    = Google.Rpc.Code.Fai.InvalidArgument,
        			Message = dex.Message
        		}
        	};
        }
        catch (Exception) {
        	return new CommandResult {
        		RequestId = request.RequestId,
        		// Status = new Status {
        		// 	//Code    = Code.Internal,
        		// 	Message = ex.Message
        		// },
        	};
        }



		// try {
		// 	var result = await execute;
		//
		// 	return new CommandResult {
		// 		RequestId = request.RequestId,
		// 		// Status = new Status {
		// 		// 	//Code    = Code.Ok,
		// 		// 	Message = null
		// 		// },
		// 		//Events = { ConvertChangesToEvents(result.Changes).ToArray() }
		// 	};
		// }
		// catch (DomainException dex) {
		// 	// return a failed result
		// 	return new CommandResult {
		// 		RequestId = request.RequestId,
		// 		// Status = new Status {
		// 		// 	//Code    = Code.InvalidArgument,
		// 		// 	Message = dex.Message
		// 		// }
		// 	};
		// }
		// catch (Exception ex) {
		// 	return new CommandResult {
		// 		RequestId = request.RequestId,
		// 		// Status = new Status {
		// 		// 	//Code    = Code.Internal,
		// 		// 	Message = ex.Message
		// 		// },
		// 	};
		// }

		//
		// IEnumerable<ConnectorEvent> ConvertChangesToEvents(IEnumerable<Change>? changes) {
		// 	foreach (var change in changes ?? []) {
		// 		ConnectorEvent evt = change.Event switch {
		// 			ConnectorCreated x         => new() { Created         = x },
		// 			ConnectorDeleted x         => new() { Deleted         = x },
		// 			ConnectorSettingsUpdated x => new() { SettingsUpdated = x },
		// 			ConnectorEnabled x         => new() { Enabled         = x },
		// 			ConnectorDisabled x        => new() { Disabled        = x },
		// 			ConnectorStatusChanged x   => new() { StatusChanged   = x },
		// 			ConnectorPositionReset x   => new() { PositionReset   = x },
		//
		// 			// ConnectorNotFound x        => new() { CommandFailed = new() { NotFound        = x } },
		// 			// AccessDenied x             => new() { CommandFailed = new() { AccessDenied    = x } },
		// 			// InvalidConnectorSettings x => new() { CommandFailed = new() { InvalidSettings = x } },
		// 			// ConnectorStartTimeout x    => new() { CommandFailed = new() { StartTimeout    = x } },
		// 			// ConnectorStopTimeout x     => new() { CommandFailed = new() { StopTimeout     = x } },
		//
		// 			_ => throw new ArgumentOutOfRangeException()
		// 		};
		//
		// 		yield return evt;
		// 	}
		// }
	}
}