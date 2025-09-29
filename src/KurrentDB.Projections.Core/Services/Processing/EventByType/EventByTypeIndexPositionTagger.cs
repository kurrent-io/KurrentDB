// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.EventByType;

public class EventByTypeIndexPositionTagger : PositionTagger {
	private readonly HashSet<string> _streams;
	private readonly HashSet<string> _eventTypes;
	private readonly Dictionary<string, string> _streamToEventType;

	public EventByTypeIndexPositionTagger(
		int phase, string[] eventTypes, bool includeStreamDeletedNotification = false)
		: base(phase) {
		ArgumentNullException.ThrowIfNull(eventTypes);
		if (eventTypes.Length == 0)
			throw new ArgumentException("eventTypes");
		_eventTypes = new(eventTypes);
		if (includeStreamDeletedNotification)
			_eventTypes.Add("$deleted");
		_streams = new(eventTypes.Select(eventType => $"$et-{eventType}"));
		_streamToEventType = eventTypes.ToDictionary(v => $"$et-{v}", v => v);
	}

	public override bool IsMessageAfterCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase < Phase)
			return true;
		if (previous.Mode_ != CheckpointTag.Mode.EventTypeIndex)
			throw new ArgumentException("Mode.EventTypeIndex expected", nameof(previous));
		if (committedEvent.Data.EventOrLinkTargetPosition.CommitPosition <= 0)
			throw new ArgumentException("complete TF position required", nameof(committedEvent));

		return committedEvent.Data.EventOrLinkTargetPosition > previous.Position;
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
		if (previous.Phase != Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase.  Expected: {Phase} Was: {previous.Phase}");

		if (committedEvent.Data.EventOrLinkTargetPosition < previous.Position)
			throw new InvalidOperationException(
				$"Cannot make a checkpoint tag at earlier position. '{committedEvent.Data.EventOrLinkTargetPosition}' < '{previous.Position}'");
		var byIndex = _streams.Contains(committedEvent.Data.PositionStreamId);
		return byIndex
			? previous.UpdateEventTypeIndexPosition(
				committedEvent.Data.EventOrLinkTargetPosition,
				_streamToEventType[committedEvent.Data.PositionStreamId],
				committedEvent.Data.PositionSequenceNumber)
			: previous.UpdateEventTypeIndexPosition(committedEvent.Data.EventOrLinkTargetPosition);
	}

	public override CheckpointTag MakeCheckpointTag(
		CheckpointTag previous, ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
		if (previous.Phase != Phase)
			throw new ArgumentException($"Invalid checkpoint tag phase.  Expected: {Phase} Was: {previous.Phase}");

		if (partitionDeleted.DeleteEventOrLinkTargetPosition < previous.Position)
			throw new InvalidOperationException(
				$"Cannot make a checkpoint tag at earlier position. '{partitionDeleted.DeleteEventOrLinkTargetPosition}' < '{previous.Position}'");
		var byIndex = _streams.Contains(partitionDeleted.PositionStreamId);
		//TODO: handle invalid partition deleted messages without required values
		return byIndex
			? previous.UpdateEventTypeIndexPosition(
				partitionDeleted.DeleteEventOrLinkTargetPosition.Value,
				_streamToEventType[partitionDeleted.PositionStreamId],
				partitionDeleted.PositionEventNumber.Value)
			: previous.UpdateEventTypeIndexPosition(partitionDeleted.DeleteEventOrLinkTargetPosition.Value);
	}

	public override CheckpointTag MakeZeroCheckpointTag() {
		return CheckpointTag.FromEventTypeIndexPositions(
			Phase, new TFPos(0, -1), _eventTypes.ToDictionary(v => v, _ => ExpectedVersion.NoStream));
	}

	public override bool IsCompatible(CheckpointTag checkpointTag) {
		//TODO: should Stream be supported here as well if in the set?
		return checkpointTag.Mode_ == CheckpointTag.Mode.EventTypeIndex
			   && checkpointTag.Streams.All(v => _eventTypes.Contains(v.Key));
	}

	public override CheckpointTag AdjustTag(CheckpointTag tag) {
		if (tag.Phase < Phase)
			return tag;
		if (tag.Phase > Phase)
			throw new ArgumentException(
				$"Invalid checkpoint tag phase.  Expected less or equal to: {Phase} Was: {tag.Phase}", nameof(tag));

		if (tag.Mode_ == CheckpointTag.Mode.EventTypeIndex) {
			return CheckpointTag.FromEventTypeIndexPositions(
				tag.Phase, tag.Position,
				_eventTypes.ToDictionary(v => v, v => tag.Streams.TryGetValue(v, out var p) ? p : -1));
		}

		return tag.Mode_ switch {
			CheckpointTag.Mode.MultiStream => throw new NotSupportedException(
				"Conversion from MultiStream to EventTypeIndex position tag is not supported"),
			CheckpointTag.Mode.Stream => throw new NotSupportedException(
				"Conversion from Stream to EventTypeIndex position tag is not supported"),
			CheckpointTag.Mode.PreparePosition => throw new NotSupportedException(
				"Conversion from PreparePosition to EventTypeIndex position tag is not supported"),
			CheckpointTag.Mode.Position => CheckpointTag.FromEventTypeIndexPositions(tag.Phase, tag.Position,
				_eventTypes.ToDictionary(v => v, _ => (long)-1)),
			_ => throw new NotSupportedException(
				$"The given checkpoint is invalid. Possible causes might include having written an event to the projection's managed stream. The bad checkpoint: {tag}")
		};
	}
}
