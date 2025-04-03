// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Standard;
using Newtonsoft.Json.Linq;

namespace EventStore.Projections.Core.Services.Processing;

public class ResolvedEvent {
	private readonly string _eventStreamId;
	private readonly long _eventSequenceNumber;
	private readonly bool _resolvedLinkTo;

	private readonly string _positionStreamId;
	private readonly long _positionSequenceNumber;
	private readonly TFPos _position;
	private readonly TFPos _eventOrLinkTargetPosition;
	private readonly TFPos _linkOrEventPosition;


	public readonly Guid EventId;
	public readonly string EventType;
	public readonly bool IsJson;
	public readonly DateTime Timestamp;

	public readonly ReadOnlyMemory<byte> DataMemory;

	public readonly string Data;
	public readonly string Metadata;
	public readonly string PositionMetadata;
	public readonly string StreamMetadata;
	public readonly bool IsLinkToDeletedStream;
	public readonly bool IsLinkToDeletedStreamTombstone;

	public ResolvedEvent(EventStore.Core.Data.ResolvedEvent resolvedEvent, byte[] streamMetadata) {
		var positionEvent = resolvedEvent.Link ?? resolvedEvent.Event;
		_linkOrEventPosition = resolvedEvent.OriginalPosition.GetValueOrDefault();
		var @event = resolvedEvent.Event;
		_positionStreamId = positionEvent.EventStreamId;
		_positionSequenceNumber = positionEvent.EventNumber;
		_eventStreamId = @event != null ? @event.EventStreamId : null;
		_eventSequenceNumber = @event != null ? @event.EventNumber : -1;
		_resolvedLinkTo = positionEvent != @event;
		_position = resolvedEvent.OriginalPosition ?? new TFPos(-1, positionEvent.LogPosition);
		EventId = @event != null ? @event.EventId : Guid.Empty;
		EventType = @event != null ? @event.EventType : null;
		IsJson = @event != null && (@event.Flags & PrepareFlags.IsJson) != 0;
		Timestamp = positionEvent.TimeStamp;

		DataMemory = @event?.Data ?? ReadOnlyMemory<byte>.Empty;

		//TODO: handle utf-8 conversion exception
		Data = @event != null && @event.Data.Length > 0 ? Helper.UTF8NoBom.GetString(@event.Data.Span) : null;
		Metadata = @event != null && @event.Metadata.Length > 0 ? Helper.UTF8NoBom.GetString(@event.Metadata.Span) : null;
		PositionMetadata = _resolvedLinkTo
			? (positionEvent.Metadata.Length > 0 ? Helper.UTF8NoBom.GetString(positionEvent.Metadata.Span) : null)
			: null;
		StreamMetadata = streamMetadata != null ? Helper.UTF8NoBom.GetString(streamMetadata) : null;

		TFPos eventOrLinkTargetPosition;
		if (_resolvedLinkTo) {
			Dictionary<string, JToken> extraMetadata = null;
			if (positionEvent.Metadata.Length > 0 && positionEvent.Metadata.Length > 0) {
				//TODO: parse JSON only when unresolved link and just tag otherwise
				CheckpointTag tag;
				if (resolvedEvent.Link != null && resolvedEvent.Event == null) {
					var checkpointTagJson =
						positionEvent.Metadata.ParseCheckpointTagVersionExtraJson(default(ProjectionVersion));
					tag = checkpointTagJson.Tag;
					extraMetadata = checkpointTagJson.ExtraMetadata;

					var parsedPosition = tag.Position;

					eventOrLinkTargetPosition = parsedPosition != new TFPos(long.MinValue, long.MinValue)
						? parsedPosition
						: new TFPos(-1, positionEvent.LogPosition);
				} else {
					tag = positionEvent.Metadata.ParseCheckpointTagJson();
					var parsedPosition = tag.Position;
					if (parsedPosition == new TFPos(long.MinValue, long.MinValue) &&
					    @event.Metadata.IsValidUtf8Json()) {
						tag = @event.Metadata.ParseCheckpointTagJson();
						if (tag != null) {
							parsedPosition = tag.Position;
						}
					}

					eventOrLinkTargetPosition = parsedPosition != new TFPos(long.MinValue, long.MinValue)
						? parsedPosition
						: new TFPos(-1, resolvedEvent.Event.LogPosition);
				}
			} else {
				eventOrLinkTargetPosition = @event != null
					? new TFPos(-1, @event.LogPosition)
					: new TFPos(-1, positionEvent.LogPosition);
			}

			JToken deletedValue;
			IsLinkToDeletedStreamTombstone = extraMetadata != null
			                                 && extraMetadata.TryGetValue("$deleted", out deletedValue);
			if (resolvedEvent.ResolveResult == ReadEventResult.NoStream
			    || resolvedEvent.ResolveResult == ReadEventResult.StreamDeleted || IsLinkToDeletedStreamTombstone) {
				IsLinkToDeletedStream = true;
				var streamId = SystemEventTypes.StreamReferenceEventToStreamId(
					SystemEventTypes.LinkTo, resolvedEvent.Link.Data);
				_eventStreamId = streamId;
			}
		} else {
			// not a link
			eventOrLinkTargetPosition = resolvedEvent.OriginalPosition ?? new TFPos(-1, positionEvent.LogPosition);
		}

		_eventOrLinkTargetPosition = eventOrLinkTargetPosition;
	}

	// Called from tests only
	public ResolvedEvent(
		string positionStreamId, long positionSequenceNumber, string eventStreamId, long eventSequenceNumber,
		bool resolvedLinkTo, TFPos position, TFPos eventOrLinkTargetPosition, Guid eventId, string eventType,
		bool isJson, byte[] data,
		byte[] metadata, byte[] positionMetadata, byte[] streamMetadata, DateTime timestamp) {
		_positionStreamId = positionStreamId;
		_positionSequenceNumber = positionSequenceNumber;
		_eventStreamId = eventStreamId;
		_eventSequenceNumber = eventSequenceNumber;
		_resolvedLinkTo = resolvedLinkTo;
		_position = position;
		_eventOrLinkTargetPosition = eventOrLinkTargetPosition;
		EventId = eventId;
		EventType = eventType;
		IsJson = isJson;
		Timestamp = timestamp;

		DataMemory = data ?? ReadOnlyMemory<byte>.Empty;

		//TODO: handle utf-8 conversion exception
		Data = data != null ? Helper.UTF8NoBom.GetString(data) : null;
		Metadata = metadata != null ? Helper.UTF8NoBom.GetString(metadata) : null;
		PositionMetadata = positionMetadata != null ? Helper.UTF8NoBom.GetString(positionMetadata) : null;
		StreamMetadata = streamMetadata != null ? Helper.UTF8NoBom.GetString(streamMetadata) : null;
	}

	// Called from tests only
	public ResolvedEvent(
		string positionStreamId, long positionSequenceNumber, string eventStreamId, long eventSequenceNumber,
		bool resolvedLinkTo, TFPos position, Guid eventId, string eventType, bool isJson, string data,
		string metadata, string positionMetadata = null, string streamMetadata = null) {
		DateTime timestamp = default(DateTime);
		if (Guid.Empty == eventId)
			throw new ArgumentException("Empty eventId provided.");
		if (string.IsNullOrEmpty(eventType))
			throw new ArgumentException("Empty eventType provided.");

		_positionStreamId = positionStreamId;
		_positionSequenceNumber = positionSequenceNumber;
		_eventStreamId = eventStreamId;
		_eventSequenceNumber = eventSequenceNumber;
		_resolvedLinkTo = resolvedLinkTo;
		_position = position;
		EventId = eventId;
		EventType = eventType;
		IsJson = isJson;
		Timestamp = timestamp;

		DataMemory = data is not null ? Helper.UTF8NoBom.GetBytes(data) : ReadOnlyMemory<byte>.Empty;
		Data = data;
		Metadata = metadata;
		PositionMetadata = positionMetadata;
		StreamMetadata = streamMetadata;
	}

	public string EventStreamId {
		get { return _eventStreamId; }
	}

	public long EventSequenceNumber {
		get { return _eventSequenceNumber; }
	}

	public bool ResolvedLinkTo {
		get { return _resolvedLinkTo; }
	}

	public string PositionStreamId {
		get { return _positionStreamId; }
	}

	public long PositionSequenceNumber {
		get { return _positionSequenceNumber; }
	}

	public TFPos Position {
		get { return _position; }
	}

	public TFPos EventOrLinkTargetPosition {
		get { return _eventOrLinkTargetPosition; }
	}

	public TFPos LinkOrEventPosition {
		get { return _linkOrEventPosition; }
	}

	public bool IsStreamDeletedEvent {
		get {
			string temp;
			return StreamDeletedHelper.IsStreamDeletedEvent(EventStreamId, EventType, Data, out temp);
		}
	}
}
