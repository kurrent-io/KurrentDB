// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Standard;
using Newtonsoft.Json.Linq;

namespace KurrentDB.Projections.Core.Services.Processing;

public class ResolvedEvent {
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

	public ResolvedEvent(KurrentDB.Core.Data.ResolvedEvent resolvedEvent, byte[] streamMetadata) {
		var positionEvent = resolvedEvent.Link ?? resolvedEvent.Event;
		LinkOrEventPosition = resolvedEvent.OriginalPosition.GetValueOrDefault();
		var @event = resolvedEvent.Event;
		PositionStreamId = positionEvent.EventStreamId;
		PositionSequenceNumber = positionEvent.EventNumber;
		EventStreamId = @event != null ? @event.EventStreamId : null;
		EventSequenceNumber = @event != null ? @event.EventNumber : -1;
		ResolvedLinkTo = positionEvent != @event;
		Position = resolvedEvent.OriginalPosition ?? new TFPos(-1, positionEvent.LogPosition);
		EventId = @event != null ? @event.EventId : Guid.Empty;
		EventType = @event != null ? @event.EventType : null;
		IsJson = @event != null && (@event.Flags & PrepareFlags.IsJson) != 0;
		Timestamp = positionEvent.TimeStamp;

		DataMemory = @event?.Data ?? ReadOnlyMemory<byte>.Empty;

		//TODO: handle utf-8 conversion exception
		Data = @event != null && @event.Data.Length > 0 ? Helper.UTF8NoBom.GetString(@event.Data.Span) : null;
		Metadata = @event != null && @event.Metadata.Length > 0 ? Helper.UTF8NoBom.GetString(@event.Metadata.Span) : null;
		PositionMetadata = ResolvedLinkTo
			? (positionEvent.Metadata.Length > 0 ? Helper.UTF8NoBom.GetString(positionEvent.Metadata.Span) : null)
			: null;
		StreamMetadata = streamMetadata != null ? Helper.UTF8NoBom.GetString(streamMetadata) : null;

		TFPos eventOrLinkTargetPosition;
		if (ResolvedLinkTo) {
			Dictionary<string, JToken> extraMetadata = null;
			if (positionEvent.Metadata.Length > 0) {
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

			IsLinkToDeletedStreamTombstone = extraMetadata != null && extraMetadata.TryGetValue("$deleted", out _);
			if (resolvedEvent.ResolveResult == ReadEventResult.NoStream
				|| resolvedEvent.ResolveResult == ReadEventResult.StreamDeleted || IsLinkToDeletedStreamTombstone) {
				IsLinkToDeletedStream = true;
				EventStreamId = SystemEventTypes.StreamReferenceEventToStreamId(SystemEventTypes.LinkTo, resolvedEvent.Link.Data);
			}
		} else {
			// not a link
			eventOrLinkTargetPosition = resolvedEvent.OriginalPosition ?? new TFPos(-1, positionEvent.LogPosition);
		}

		EventOrLinkTargetPosition = eventOrLinkTargetPosition;
	}

	// Called from tests only
	public ResolvedEvent(
		string positionStreamId, long positionSequenceNumber, string eventStreamId, long eventSequenceNumber,
		bool resolvedLinkTo, TFPos position, TFPos eventOrLinkTargetPosition, Guid eventId, string eventType,
		bool isJson, byte[] data,
		byte[] metadata, byte[] positionMetadata, byte[] streamMetadata, DateTime timestamp) {
		PositionStreamId = positionStreamId;
		PositionSequenceNumber = positionSequenceNumber;
		EventStreamId = eventStreamId;
		EventSequenceNumber = eventSequenceNumber;
		ResolvedLinkTo = resolvedLinkTo;
		Position = position;
		EventOrLinkTargetPosition = eventOrLinkTargetPosition;
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

		PositionStreamId = positionStreamId;
		PositionSequenceNumber = positionSequenceNumber;
		EventStreamId = eventStreamId;
		EventSequenceNumber = eventSequenceNumber;
		ResolvedLinkTo = resolvedLinkTo;
		Position = position;
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

	public string EventStreamId { get; }

	public long EventSequenceNumber { get; }

	public bool ResolvedLinkTo { get; }

	public string PositionStreamId { get; }

	public long PositionSequenceNumber { get; }

	public TFPos Position { get; }

	public TFPos EventOrLinkTargetPosition { get; }

	public TFPos LinkOrEventPosition { get; }

	public bool IsStreamDeletedEvent => StreamDeletedHelper.IsStreamDeletedEvent(EventStreamId, EventType, Data, out _);
}
