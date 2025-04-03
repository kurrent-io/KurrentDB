// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;

namespace EventStore.Core.Services.Storage.ReaderIndex;

public struct IndexReadEventResult {
	public readonly ReadEventResult Result;
	public readonly EventRecord Record;
	public readonly StreamMetadata Metadata;
	public readonly long LastEventNumber;
	public readonly bool? OriginalStreamExists;

	public IndexReadEventResult(ReadEventResult result, StreamMetadata metadata, long lastEventNumber, bool? originalStreamExists) {
		if (result == ReadEventResult.Success)
			throw new ArgumentException($"Wrong ReadEventResult provided for failure constructor: {result}.", nameof(result));

		Result = result;
		Record = null;
		Metadata = metadata;
		LastEventNumber = lastEventNumber;
		OriginalStreamExists = originalStreamExists;
	}

	public IndexReadEventResult(ReadEventResult result, EventRecord record, StreamMetadata metadata,
		long lastEventNumber, bool? originalStreamExists) {
		Result = result;
		Record = record;
		Metadata = metadata;
		LastEventNumber = lastEventNumber;
		OriginalStreamExists = originalStreamExists;
	}
}
