// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.POC.IO.Core;

public class EventToWrite {
	public Guid EventId { get; init; }
	public string EventType { get; init; }
	public string ContentType { get; init; }
	public ReadOnlyMemory<byte> Data { get; init; }
	public bool IsPropertyMetadata { get; init; }
	public ReadOnlyMemory<byte> Metadata { get; init; }

	public EventToWrite(
		Guid eventId,
		string eventType,
		string contentType,
		ReadOnlyMemory<byte> data,
		bool isPropertyMetadata,
		ReadOnlyMemory<byte> metadata) {

		EventId = eventId;
		EventType = eventType;
		ContentType = contentType;
		Data = data;
		IsPropertyMetadata = isPropertyMetadata;
		Metadata = metadata;
	}
}
