// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Client.Projections;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using static EventStore.Client.Projections.StatisticsReq.Types.Options;

// ReSharper disable CheckNamespace

namespace EventStore.Projections.Core.Services.Grpc;

internal partial class ProjectionManagement {
	private static readonly Operation StatisticsOperation = new(Operations.Projections.Statistics);

	public override async Task Statistics(StatisticsReq request,
		IServerStreamWriter<StatisticsResp> responseStream,
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
			_ => default(ProjectionMode?)
		};

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, mode, name));

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
			await responseStream.WriteAsync(new() { Details = stats });
		}

		return;

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
