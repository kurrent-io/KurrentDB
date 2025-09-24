// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Services;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Standard;

public class IndexStreams : IProjectionStateHandler {
	// ReSharper disable once UnusedParameter.Local
	public IndexStreams(string source, Action<string, object[]> logger) {
		var trimmedSource = source?.Trim();
		if (!string.IsNullOrEmpty(trimmedSource))
			throw new InvalidOperationException("Cannot initialize categorize stream projection handler.  No source is allowed.");
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
		if (data.PositionSequenceNumber != 0)
			return false; // not our event

		emittedEvents = [
			new(
				new EmittedDataEvent(
					SystemStreams.StreamsStream, Guid.NewGuid(), SystemEventTypes.LinkTo, false,
					$"{data.PositionSequenceNumber}@{data.PositionStreamId}", null, eventPosition,
					expectedTag: null))
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
