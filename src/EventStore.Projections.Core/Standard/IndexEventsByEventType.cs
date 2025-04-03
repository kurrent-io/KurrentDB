// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EventStore.Projections.Core.Standard;

public class IndexEventsByEventType : IProjectionStateHandler, IProjectionCheckpointHandler {
	private readonly string _indexStreamPrefix;
	private readonly string _indexCheckpointStream;

	public IndexEventsByEventType(string source, Action<string, object[]> logger) {
		if (!string.IsNullOrWhiteSpace(source))
			throw new InvalidOperationException("Empty source expected");
		if (logger != null) {
//                logger("Index events by event type projection handler has been initialized");
		}

		// we will need to declare event types we are interested in
		_indexStreamPrefix = "$et-";
		_indexCheckpointStream = "$et";
	}

	public void ConfigureSourceProcessingStrategy(SourceDefinitionBuilder builder) {
		builder.FromAll();
		builder.AllEvents();
	}

	public void Load(string state) {
	}

	public void LoadShared(string state) {
		throw new NotImplementedException();
	}

	public void Initialize() {
	}

	public void InitializeShared() {
	}

	public string GetStatePartition(CheckpointTag eventPosition, string category, ResolvedEvent data) {
		throw new NotImplementedException();
	}

	public bool ProcessEvent(
		string partition, CheckpointTag eventPosition, string category1, ResolvedEvent data,
		out string newState, out string newSharedState, out EmittedEventEnvelope[] emittedEvents) {
		newSharedState = null;
		emittedEvents = null;
		newState = null;
		if (data.EventStreamId != data.PositionStreamId)
			return false;
		var indexedEventType = data.EventType;
		if (indexedEventType == "$>")
			return false;

		string positionStreamId;
		var isStreamDeletedEvent = StreamDeletedHelper.IsStreamDeletedEvent(
			data.PositionStreamId, data.EventType, data.Data, out positionStreamId);
		if (isStreamDeletedEvent)
			indexedEventType = "$deleted";

		emittedEvents = new[] {
			new EmittedEventEnvelope(
				new EmittedDataEvent(
					_indexStreamPrefix + indexedEventType, Guid.NewGuid(), "$>", false,
					data.EventSequenceNumber + "@" + positionStreamId,
					isStreamDeletedEvent
						? new ExtraMetaData(new Dictionary<string, JRaw> {{"$deleted", new JRaw(-1)}})
						: null, eventPosition, expectedTag: null))
		};

		return true;
	}

	public bool ProcessPartitionCreated(string partition, CheckpointTag createPosition, ResolvedEvent data,
		out EmittedEventEnvelope[] emittedEvents) {
		emittedEvents = null;
		return false;
	}

	public bool ProcessPartitionDeleted(string partition, CheckpointTag deletePosition, out string newState) {
		throw new NotImplementedException();
	}

	public string TransformStateToResult() {
		throw new NotImplementedException();
	}

	public void Dispose() {
	}

	public void ProcessNewCheckpoint(CheckpointTag checkpointPosition, out EmittedEventEnvelope[] emittedEvents) {
		emittedEvents = new[] {
			new EmittedEventEnvelope(
				new EmittedDataEvent(
					_indexCheckpointStream, Guid.NewGuid(), ProjectionEventTypes.PartitionCheckpoint,
					true, checkpointPosition.ToJsonString(), null, checkpointPosition, expectedTag: null))
		};
	}

	public IQuerySources GetSourceDefinition() {
		return SourceDefinitionBuilder.From(ConfigureSourceProcessingStrategy);
	}
}
