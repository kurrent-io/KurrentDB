// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Services;

public interface ISourceDefinitionSource {
	IQuerySources GetSourceDefinition();
}


public interface IProjectionStateHandler : IDisposable, ISourceDefinitionSource {
	void Load(string state);
	void LoadShared(string state);
	void Initialize();
	void InitializeShared();

	/// <summary>
	/// Get state partition from the event
	/// </summary>
	/// <returns>partition name</returns>
	string GetStatePartition(CheckpointTag eventPosition, string category, ResolvedEvent data);

	/// <summary>
	/// Processes event and updates internal state if necessary.
	/// </summary>
	/// <returns>true - if event was processed (new state must be returned) </returns>
	bool ProcessEvent(
		string partition, CheckpointTag eventPosition, string category, ResolvedEvent @event, out string newState,
		out string newSharedState, out EmittedEventEnvelope[] emittedEvents);

	/// <summary>
	/// Processes partition created notification and updates internal state if necessary.
	/// </summary>
	/// <param name="partition"></param>
	/// <param name="createPosition"></param>
	/// <param name="event"></param>
	/// <param name="emittedEvents"></param>
	/// <returns>true - if notification was processed (new state must be returned)</returns>
	bool ProcessPartitionCreated(
		string partition, CheckpointTag createPosition, ResolvedEvent @event,
		out EmittedEventEnvelope[] emittedEvents);

	/// <summary>
	/// Processes partition deleted notification and updates internal state if necessary.
	/// </summary>
	/// <returns>true - if event was processed (new state must be returned) </returns>
	bool ProcessPartitionDeleted(string partition, CheckpointTag deletePosition, out string newState);

	/// <summary>
	/// Transforms current state into a projection result.  Should not call any emit/linkTo etc
	/// </summary>
	/// <returns>result JSON or NULL if current state has been skipped</returns>
	string TransformStateToResult();
}

public interface IProjectionCheckpointHandler {
	void ProcessNewCheckpoint(CheckpointTag checkpointPosition, out EmittedEventEnvelope[] emittedEvents);
}

public static class ProjectionStateHandlerTestExtensions {
	public static bool ProcessEvent(
		this IProjectionStateHandler self, string partition, CheckpointTag eventPosition, string streamId,
		string eventType, string category, Guid eventId, long eventSequenceNumber, string metadata, string data,
		out string state, out EmittedEventEnvelope[] emittedEvents, bool isJson = true) {
		string ignoredSharedState;
		return self.ProcessEvent(
			partition, eventPosition, category,
			new ResolvedEvent(
				streamId, eventSequenceNumber, streamId, eventSequenceNumber, false, new TFPos(0, -1), eventId,
				eventType, isJson, data, metadata), out state, out ignoredSharedState, out emittedEvents);
	}

	public static bool ProcessEvent(
		this IProjectionStateHandler self, string partition, CheckpointTag eventPosition, string streamId,
		string eventType, string category, Guid eventId, long eventSequenceNumber, string metadata, string data,
		out string state, out string sharedState, out EmittedEventEnvelope[] emittedEvents, bool isJson = true) {
		return self.ProcessEvent(
			partition, eventPosition, category,
			new ResolvedEvent(
				streamId, eventSequenceNumber, streamId, eventSequenceNumber, false, new TFPos(0, -1), eventId,
				eventType, isJson, data, metadata), out state, out sharedState, out emittedEvents);
	}

	public static string GetNativeHandlerName(this Type handlerType) {
		return "native:" + handlerType.Namespace + "." + handlerType.Name;
	}
}
