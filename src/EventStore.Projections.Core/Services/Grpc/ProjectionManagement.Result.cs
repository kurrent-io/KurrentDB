// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Threading.Tasks;
using EventStore.Core.Messaging;
using EventStore.Client.Projections;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation ResultOperation = new Operation(Operations.Projections.Result);
	private static readonly Operation StateOperation = new Operation(Operations.Projections.State);
	public override async Task<ResultResp> Result(ResultReq request, ServerCallContext context) {

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ResultOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var resultSource = new TaskCompletionSource<Value>();
		var options = request.Options;

		var name = options.Name;
		var partition = options.Partition ?? string.Empty;

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new ProjectionManagementMessage.Command.GetResult(envelope, name, partition));

		return new ResultResp {
			Result = await resultSource.Task
		};

		void OnMessage(Message message) {
			switch (message) {
				case ProjectionManagementMessage.ProjectionResult result:
					if (string.IsNullOrEmpty(result.Result)) {
						resultSource.TrySetResult(new Value {
							StructValue = new Struct()
						});
					} else {
						var document = JsonDocument.Parse(result.Result);
						resultSource.TrySetResult(GetProtoValue(document.RootElement));
					}
					break;
				case ProjectionManagementMessage.NotFound:
					resultSource.TrySetException(ProjectionNotFound(name));
					break;
				default:
					resultSource.TrySetException(UnknownMessage<ProjectionManagementMessage.ProjectionResult>(message));
					break;
			}
		}
	}

	public override async Task<StateResp> State(StateReq request, ServerCallContext context) {

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, StateOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		var resultSource = new TaskCompletionSource<Value>();

		var options = request.Options;

		var name = options.Name;
		var partition = options.Partition ?? string.Empty;

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new ProjectionManagementMessage.Command.GetState(envelope, name, partition));

		return new StateResp {
			State = await resultSource.Task
		};

		void OnMessage(Message message) {
			switch (message) {
				case ProjectionManagementMessage.ProjectionState result:
					if (string.IsNullOrEmpty(result.State)) {
						resultSource.TrySetResult(new Value {
							StructValue = new Struct()
						});
					} else {
						var document = JsonDocument.Parse(result.State);
						resultSource.TrySetResult(GetProtoValue(document.RootElement));
					}
					break;
				case ProjectionManagementMessage.NotFound:
					resultSource.TrySetException(ProjectionNotFound(name));
					break;
				default:
					resultSource.TrySetException(UnknownMessage<ProjectionManagementMessage.ProjectionState>(message));
					break;
			}
		}
	}
}
