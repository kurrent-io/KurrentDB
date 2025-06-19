// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.POC.IO.Core;

public class Event {
	public Guid EventId { get; init; }
	public DateTime Created { get; init; }
	public string Stream { get; init; }
	public ulong EventNumber { get; init; }
	public string EventType { get; init; }
	public string ContentType { get; init; }
	public ulong CommitPosition { get; init; }
	public ulong PreparePosition { get; init; }
	public bool IsRedacted { get; init; }
	public ReadOnlyMemory<byte> Data { get; init; }
	public ReadOnlyMemory<byte> Metadata { get; init; }

	public Event(
		Guid eventId,
		DateTime created,
		string stream,
		ulong eventNumber,
		string eventType,
		string contentType,
		ulong commitPosition,
		ulong preparePosition,
		bool isRedacted,
		ReadOnlyMemory<byte> data,
		ReadOnlyMemory<byte> metadata) {

		EventId = eventId;
		Created = created;
		Stream = stream;
		EventNumber = eventNumber;
		EventType = eventType;
		ContentType = contentType;
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
		IsRedacted = isRedacted;
		Data = data;
		Metadata = metadata;
	}
}
