// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.Jint;

public abstract class specification_with_event_handled : TestFixtureWithInterpretedProjection {
	protected ResolvedEvent _handledEvent;
	protected string _newState;
	protected string _newSharedState;
	protected EmittedEventEnvelope[] _emittedEventEnvelopes;

	protected override void When() {
		_stateHandler.ProcessEvent(
			"",
			CheckpointTag.FromPosition(
				0, _handledEvent.Position.CommitPosition, _handledEvent.Position.PreparePosition), "",
			_handledEvent,
			out _newState, out _newSharedState, out _emittedEventEnvelopes);
	}

	protected static ResolvedEvent CreateSampleEvent(
		string streamId, int sequenceNumber, string eventType, string data, TFPos tfPos) {
		return new ResolvedEvent(
			streamId, sequenceNumber, streamId, sequenceNumber, true, tfPos, Guid.NewGuid(), eventType, true, data,
			"{}", "{\"position_meta\":1}");
	}
}
