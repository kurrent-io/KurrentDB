// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;

namespace KurrentDB.Components.Projections;

// Publishes to the projections subsystem's leader input queue (supplied by the DI factory in Program.cs).
// The queue is null when projections are disabled on this node (e.g. --run-projections=None); the service
// then reports itself unavailable rather than failing to resolve, so the page can explain the situation.
public class ProjectionsService(IPublisher publisher, IAuthorizationProvider authorizer) {
	// False when the projections subsystem isn't running on this node. Every command path goes through
	// Authorize, which refuses to publish in that case, so callers can rely on this for UX without each
	// guarding individually.
	public bool Available => publisher is not null;

	// Operations match the configured authorization policy for projections (claim-based, not resource-scoped).
	// The projection management commands also carry RunAs (a core check); these add the pluggable-policy check
	// the gRPC/HTTP front-ends do, and cover the stats/state reads that carry no RunAs at all.
	static readonly Operation StatisticsOp = new(Operations.Projections.Statistics);
	static readonly Operation ReadOp = new(Operations.Projections.Read);
	static readonly Operation StateOp = new(Operations.Projections.State);
	static readonly Operation CreateOp = new(Operations.Projections.Create);
	static readonly Operation EnableOp = new(Operations.Projections.Enable);
	static readonly Operation DisableOp = new(Operations.Projections.Disable);
	static readonly Operation ResetOp = new(Operations.Projections.Reset);
	static readonly Operation DeleteOp = new(Operations.Projections.Delete);
	static readonly Operation UpdateOp = new(Operations.Projections.Update);
	static readonly Operation ReadConfigOp = new(Operations.Projections.ReadConfiguration);
	static readonly Operation UpdateConfigOp = new(Operations.Projections.UpdateConfiguration);
	static readonly Operation RestartOp = new(Operations.Projections.Restart);

	public async ValueTask<ProjectionStatistics[]> GetAllAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, StatisticsOp, ct);
		var envelope = new TcsEnvelope<ProjectionManagementMessage.Statistics>();
		publisher.Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, ProjectionMode.AllNonTransient, null));
		var result = await envelope.Task.WaitAsync(ct);
		return result.Projections ?? [];
	}

	public async ValueTask<ProjectionStatistics[]> GetStatisticsAsync(ClaimsPrincipal principal, string name, CancellationToken ct) {
		await Authorize(principal, StatisticsOp, ct);
		var envelope = new TcsEnvelope<ProjectionManagementMessage.Statistics>();
		publisher.Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, null, name));
		var result = await envelope.Task.WaitAsync(ct);
		return result.Projections ?? [];
	}

	public async ValueTask<string> GetQueryAsync(ClaimsPrincipal principal, string name, CancellationToken ct) {
		await Authorize(principal, ReadOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.GetQuery(envelope, name,
			new ProjectionManagementMessage.RunAs(principal)));
		return await envelope.Task.WaitAsync(ct) switch {
			ProjectionManagementMessage.ProjectionQuery q => q.Query,
			ProjectionManagementMessage.NotFound => throw new ProjectionsException($"Projection '{name}' not found."),
			ProjectionManagementMessage.NotAuthorized => throw new ProjectionsException("Access denied."),
			_ => throw new ProjectionsException("Unexpected error.")
		};
	}

	public async ValueTask<string> GetStateAsync(ClaimsPrincipal principal, string name, CancellationToken ct,
		string partition = "") {

		await Authorize(principal, StateOp, ct);   // the GetState command carries no RunAs, so this is the access gate
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.GetState(envelope, name, partition));
		return await envelope.Task.WaitAsync(ct) switch {
			ProjectionManagementMessage.ProjectionState s => s.State,
			ProjectionManagementMessage.NotFound => throw new ProjectionsException($"Projection '{name}' not found."),
			_ => ""
		};
	}

	public async ValueTask CreateAsync(ClaimsPrincipal principal, string name, string query,
		ProjectionMode mode, bool emitEnabled, bool trackEmittedStreams, bool useV2, CancellationToken ct) {

		var projectionType = mode == ProjectionMode.Continuous
			? Operations.Projections.Parameters.Continuous
			: Operations.Projections.Parameters.OneTime;
		await Authorize(principal, CreateOp.WithParameter(projectionType), ct);
		var checkpointsEnabled = mode == ProjectionMode.Continuous;
		var engineVersion = useV2 ? ProjectionConstants.EngineV2 : ProjectionConstants.EngineV1;
		// v2 doesn't support tracking emitted streams.
		if (useV2)
			trackEmittedStreams = false;
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.Post(
			envelope, mode, name,
			new ProjectionManagementMessage.RunAs(principal),
			"JS", query, true, checkpointsEnabled, emitEnabled, trackEmittedStreams,
			enableRunAs: true, engineVersion: engineVersion));

		switch (await envelope.Task.WaitAsync(ct)) {
			case ProjectionManagementMessage.Updated:
				return;
			case ProjectionManagementMessage.Conflict c:
				throw new ProjectionsException($"Conflict: {c.Reason}");
			case ProjectionManagementMessage.NotAuthorized:
				throw new ProjectionsException("Access denied.");
			default:
				throw new ProjectionsException("Unexpected error.");
		}
	}

	public async ValueTask EnableAsync(ClaimsPrincipal principal, string name, CancellationToken ct) {
		await Authorize(principal, EnableOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.Enable(
			envelope, name, new ProjectionManagementMessage.RunAs(principal)));
		CheckCommandResult(name, await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask EnableAllAsync(ClaimsPrincipal principal, CancellationToken ct) {
		var projections = await GetAllAsync(principal, ct);
		foreach (var p in projections)
			await EnableAsync(principal, p.Name, ct);
	}

	public async ValueTask DisableAllAsync(ClaimsPrincipal principal, CancellationToken ct) {
		var projections = await GetAllAsync(principal, ct);
		foreach (var p in projections)
			await DisableAsync(principal, p.Name, ct);
	}

	public async ValueTask DisableAsync(ClaimsPrincipal principal, string name, CancellationToken ct) {
		await Authorize(principal, DisableOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.Disable(
			envelope, name, new ProjectionManagementMessage.RunAs(principal)));
		CheckCommandResult(name, await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask ResetAsync(ClaimsPrincipal principal, string name, CancellationToken ct) {
		await Authorize(principal, ResetOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.Reset(
			envelope, name, new ProjectionManagementMessage.RunAs(principal)));
		CheckCommandResult(name, await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask DeleteAsync(ClaimsPrincipal principal, string name,
		bool deleteCheckpointStream, bool deleteStateStream, bool deleteEmittedStreams, CancellationToken ct) {

		await Authorize(principal, DeleteOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.Delete(
			envelope, name, new ProjectionManagementMessage.RunAs(principal),
			deleteCheckpointStream, deleteStateStream, deleteEmittedStreams));
		CheckCommandResult(name, await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask UpdateQueryAsync(ClaimsPrincipal principal, string name, string query, bool? emitEnabled, CancellationToken ct) {
		await Authorize(principal, UpdateOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.UpdateQuery(
			envelope, name, new ProjectionManagementMessage.RunAs(principal), query, emitEnabled));
		CheckCommandResult(name, await envelope.Task.WaitAsync(ct));
	}

	public async ValueTask<ProjectionManagementMessage.ProjectionConfig> GetConfigAsync(
		ClaimsPrincipal principal, string name, CancellationToken ct) {

		await Authorize(principal, ReadConfigOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.GetConfig(
			envelope, name, new ProjectionManagementMessage.RunAs(principal)));
		return await envelope.Task.WaitAsync(ct) switch {
			ProjectionManagementMessage.ProjectionConfig c => c,
			ProjectionManagementMessage.NotFound => throw new ProjectionsException($"Projection '{name}' not found."),
			ProjectionManagementMessage.NotAuthorized => throw new ProjectionsException("Access denied."),
			_ => throw new ProjectionsException("Unexpected error.")
		};
	}

	public async ValueTask UpdateConfigAsync(
		ClaimsPrincipal principal, string name, bool emitEnabled, bool trackEmittedStreams,
		int checkpointAfterMs, int checkpointHandledThreshold, int checkpointUnhandledBytesThreshold,
		int pendingEventsThreshold, int maxWriteBatchLength, int maxAllowedWritesInFlight,
		int? projectionExecutionTimeout, CancellationToken ct) {

		await Authorize(principal, UpdateConfigOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionManagementMessage.Command.UpdateConfig(
			envelope, name, emitEnabled, trackEmittedStreams, checkpointAfterMs, checkpointHandledThreshold,
			checkpointUnhandledBytesThreshold, pendingEventsThreshold, maxWriteBatchLength, maxAllowedWritesInFlight,
			new ProjectionManagementMessage.RunAs(principal), projectionExecutionTimeout));
		CheckCommandResult(name, await envelope.Task.WaitAsync(ct));
	}

	// Restarts the whole projections subsystem (matches the gRPC ProjectionManagement.RestartSubsystem).
	public async ValueTask RestartSubsystemAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, RestartOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ProjectionSubsystemMessage.RestartSubsystem(envelope));
		switch (await envelope.Task.WaitAsync(ct)) {
			case ProjectionSubsystemMessage.SubsystemRestarting:
				return;
			// e.g. the subsystem is already restarting, or not in a state that can be restarted.
			case ProjectionSubsystemMessage.InvalidSubsystemRestart invalid:
				throw new ProjectionsException($"Cannot restart projections: {invalid.Reason} (state: {invalid.SubsystemState}).");
			default:
				throw new ProjectionsException("Failed to restart the projections subsystem.");
		}
	}

	// Enforce the configured authorization policy before publishing, translating a denial into the
	// ProjectionsException the components already handle.
	async ValueTask Authorize(ClaimsPrincipal principal, Operation operation, CancellationToken ct) {
		if (publisher is null)
			throw new ProjectionsException("The projections subsystem is not enabled on this server.");
		if (!await authorizer.CheckAccessAsync(principal, operation, ct))
			throw new ProjectionsException("Access denied.");
	}

	static void CheckCommandResult(string name, Message message) {
		switch (message) {
			case ProjectionManagementMessage.Updated:
				return;
			case ProjectionManagementMessage.NotFound:
				throw new ProjectionsException($"Projection '{name}' not found.");
			case ProjectionManagementMessage.NotAuthorized:
				throw new ProjectionsException("Access denied.");
			case ProjectionManagementMessage.Conflict c:
				throw new ProjectionsException($"Conflict: {c.Reason}");
			// Generic failure (NotFound/NotAuthorized derive from this, so it's matched last) —
			// surfaces e.g. "Cannot update the config of a projection that hasn't been stopped or faulted."
			case ProjectionManagementMessage.OperationFailed f:
				throw new ProjectionsException(f.Reason);
			default:
				throw new ProjectionsException("Unexpected error.");
		}
	}
}

public class ProjectionsException(string message) : Exception(message);
