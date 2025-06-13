// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using static EventStore.Client.Projections.CreateReq.Types.Options;

// ReSharper disable CheckNamespace

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation CreateOperation = new Operation(Operations.Projections.Create);

	public override async Task<CreateResp> Create(CreateReq request, ServerCallContext context) {
		var createdSource = TaskCompletionSourceFactory.CreateDefault<bool>();
		var options = request.Options;

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, CreateOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}
		const string handlerType = "JS";
		var name = options.ModeCase switch {
			ModeOneofCase.Continuous => options.Continuous.Name,
			ModeOneofCase.Transient => options.Transient.Name,
			ModeOneofCase.OneTime => Guid.NewGuid().ToString("D"),
			_ => throw new InvalidOperationException()
		};
		var projectionMode = options.ModeCase switch {
			ModeOneofCase.Continuous => ProjectionMode.Continuous,
			ModeOneofCase.Transient => ProjectionMode.Transient,
			ModeOneofCase.OneTime => ProjectionMode.OneTime,
			_ => throw new InvalidOperationException()
		};
		var emitEnabled = options.ModeCase switch {
			ModeOneofCase.Continuous => options.Continuous.EmitEnabled,
			_ => false
		};
		var trackEmittedStreams = (options.ModeCase, emitEnabled, options.Continuous?.TrackEmittedStreams) switch {
			(ModeOneofCase.Continuous, true, true) => true,
			(ModeOneofCase.Continuous, false, true) =>
				throw new InvalidOperationException("EmitEnabled must be set to true to track emitted streams."),
			_ => false
		};
		var checkpointsEnabled = options.ModeCase switch {
			ModeOneofCase.Continuous => true,
			ModeOneofCase.OneTime => false,
			ModeOneofCase.Transient => false,
			_ => throw new InvalidOperationException()
		};

		var runAs = new ProjectionManagementMessage.RunAs(user);

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new ProjectionManagementMessage.Command.Post(envelope, projectionMode, name, runAs,
			handlerType, options.Query, true, checkpointsEnabled, emitEnabled, trackEmittedStreams, true));

		await createdSource.Task;

		return new CreateResp();

		void OnMessage(Message message) {
			if (message is not ProjectionManagementMessage.Updated) {
				createdSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Updated>(message));
				return;
			}

			createdSource.TrySetResult(true);
		}
	}
}
