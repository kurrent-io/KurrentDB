// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Services;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Standard;

public class CategorizeEventsByStreamPath : IProjectionStateHandler {
	private readonly string _categoryStreamPrefix;
	private readonly StreamCategoryExtractor _streamCategoryExtractor;

	public CategorizeEventsByStreamPath(string source, Action<string, object[]> logger) {
		var extractor = StreamCategoryExtractor.GetExtractor(source);
		// we will need to declare event types we are interested in
		_categoryStreamPrefix = "$ce-";
		_streamCategoryExtractor = extractor;
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
		string partition,
		CheckpointTag eventPosition,
		string category1,
		ResolvedEvent data,
		out string newState,
		out string newSharedState,
		out EmittedEventEnvelope[] emittedEvents) {
		newSharedState = null;
		emittedEvents = null;
		newState = null;
		var isStreamDeletedEvent = StreamDeletedHelper.IsStreamDeletedEvent(
			data.PositionStreamId, data.EventType, data.Data, out var deletedStreamId);

		var category = _streamCategoryExtractor.GetCategoryByStreamId(isStreamDeletedEvent ? deletedStreamId : data.PositionStreamId);
		if (category == null)
			return true; // handled but not interesting

		var linkTarget = data.EventType == SystemEventTypes.LinkTo ? data.Data : $"{data.EventSequenceNumber}@{data.EventStreamId}";

		emittedEvents = [
			new(
				new EmittedLinkToWithRecategorization(
					_categoryStreamPrefix + category, Guid.NewGuid(), linkTarget, eventPosition, expectedTag: null,
					originalStreamId: isStreamDeletedEvent ? deletedStreamId : data.PositionStreamId,
					streamDeletedAt: isStreamDeletedEvent ? -1 : null))
		];

		return true;
	}

	public bool ProcessPartitionCreated(string partition,
		CheckpointTag createPosition,
		ResolvedEvent data,
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

		static void ConfigureSourceProcessingStrategy(SourceDefinitionBuilder builder) {
			builder.FromAll();
			builder.AllEvents();
			builder.SetIncludeLinks();
		}
	}
}
