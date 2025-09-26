// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

public class ResultEventEmitter : IResultEventEmitter {
	private readonly ProjectionNamesBuilder _namesBuilder;
	private readonly EmittedStream.WriterConfiguration.StreamMetadata _resultStreamMetadata = new( /* TBD */);

	public ResultEventEmitter(ProjectionNamesBuilder namesBuilder) {
		ArgumentNullException.ThrowIfNull(namesBuilder);
		_namesBuilder = namesBuilder;
	}

	public EmittedEventEnvelope[] ResultUpdated(string partition, string result, CheckpointTag at) {
		return CreateResultUpdatedEvents(partition, result, at);
	}

	private EmittedEventEnvelope[] CreateResultUpdatedEvents(string partition, string projectionResult, CheckpointTag at) {
		var streamId = _namesBuilder.MakePartitionResultStreamName(partition);
		if (string.IsNullOrEmpty(partition)) {
			var result =
				new EmittedEventEnvelope(
					projectionResult == null
						? new EmittedDataEvent(streamId, Guid.NewGuid(), "ResultRemoved", true, null, null, at, null)
						: new(streamId, Guid.NewGuid(), "Result", true, projectionResult, null, at, null),
					_resultStreamMetadata);

			return [result];
		} else {
			var allResultsStreamId = _namesBuilder.GetResultStreamName();
			var linkTo = new EmittedLinkTo(allResultsStreamId, Guid.NewGuid(), streamId, at, null);
			var linkToEnvelope = new EmittedEventEnvelope(linkTo, _resultStreamMetadata);
			var result =
				new EmittedEventEnvelope(
					projectionResult == null
						? new EmittedDataEvent(
							streamId, Guid.NewGuid(), "ResultRemoved", true, null, null, at, null, linkTo.SetTargetEventNumber)
						: new(streamId, Guid.NewGuid(), "Result", true, projectionResult, null, at, null, linkTo.SetTargetEventNumber),
					_resultStreamMetadata);
			return [result, linkToEnvelope];
		}
	}
}
