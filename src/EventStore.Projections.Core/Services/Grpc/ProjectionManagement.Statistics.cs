// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Messaging;
using EventStore.Client.Projections;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using Grpc.Core;
using static EventStore.Client.Projections.StatisticsReq.Types.Options;

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation StatisticsOperation = new Operation(Operations.Projections.Statistics);
	public override async Task Statistics(StatisticsReq request, IServerStreamWriter<StatisticsResp> responseStream,
		ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, StatisticsOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var statsSource = new TaskCompletionSource<ProjectionStatistics[]>();

		var options = request.Options;
		var name = string.IsNullOrEmpty(options.Name) ? null : options.Name;
		var mode = options.ModeCase switch {
			ModeOneofCase.Continuous => ProjectionMode.Continuous,
			ModeOneofCase.Transient => ProjectionMode.Transient,
			ModeOneofCase.OneTime => ProjectionMode.OneTime,
			_ => default(Nullable<ProjectionMode>)
		};

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, mode, name, true));

		foreach (var stats in Array.ConvertAll(await statsSource.Task, s => new StatisticsResp.Types.Details {
			BufferedEvents = s.BufferedEvents,
			CheckpointStatus = s.CheckpointStatus ?? string.Empty,
			CoreProcessingTime = s.CoreProcessingTime,
			EffectiveName = s.EffectiveName ?? string.Empty,
			Epoch = s.Epoch,
			EventsProcessedAfterRestart = s.EventsProcessedAfterRestart,
			LastCheckpoint = s.LastCheckpoint ?? string.Empty,
			Mode = s.Mode.ToString(),
			Name = s.Name,
			ReadsInProgress = s.ReadsInProgress,
			PartitionsCached = s.PartitionsCached,
			Position = s.Position ?? string.Empty,
			Progress = s.Progress,
			StateReason = s.StateReason ?? string.Empty,
			Status = s.Status ?? string.Empty,
			Version = s.Version,
			WritePendingEventsAfterCheckpoint = s.WritePendingEventsAfterCheckpoint,
			WritePendingEventsBeforeCheckpoint = s.WritePendingEventsBeforeCheckpoint,
			WritesInProgress = s.WritesInProgress
		})) {
			await responseStream.WriteAsync(new StatisticsResp { Details = stats });
		}

		void OnMessage(Message message) {
			switch (message) {
				case ProjectionManagementMessage.Statistics statistics:
					statsSource.TrySetResult(statistics.Projections);
					break;
				case ProjectionManagementMessage.NotFound:
					statsSource.TrySetException(ProjectionNotFound(name));
					break;
				default:
					statsSource.TrySetException(UnknownMessage<ProjectionManagementMessage.Statistics>(message));
					break;
			}
		}
	}
}
