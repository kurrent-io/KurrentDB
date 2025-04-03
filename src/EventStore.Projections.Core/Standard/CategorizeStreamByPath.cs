// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Standard;

public class CategorizeStreamByPath : IProjectionStateHandler {
	private readonly StreamCategoryExtractor _streamCategoryExtractor;

	public CategorizeStreamByPath(string source, Action<string, object[]> logger) {
		var extractor = StreamCategoryExtractor.GetExtractor(source, logger);
		// we will need to declare event types we are interested in
		_streamCategoryExtractor = extractor;
	}

	public void ConfigureSourceProcessingStrategy(SourceDefinitionBuilder builder) {
		builder.FromAll();
		builder.AllEvents();
		builder.SetIncludeLinks();
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

		if (data.PositionSequenceNumber != 0)
			return false; // not our event

		var category = _streamCategoryExtractor.GetCategoryByStreamId(data.PositionStreamId);
		if (category == null)
			return true; // handled but not interesting

		emittedEvents = new[] {
			new EmittedEventEnvelope(
				new EmittedDataEvent(
					"$category" + "-" + category, Guid.NewGuid(), SystemEventTypes.StreamReference, false,
					data.PositionStreamId, null, eventPosition, expectedTag: null))
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

	public IQuerySources GetSourceDefinition() {
		return SourceDefinitionBuilder.From(ConfigureSourceProcessingStrategy);
	}
}
