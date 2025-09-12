// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Storage;

public record struct IndexQueryRecord {
	public IndexQueryRecord(long LogPosition, long? CommitPosition, long EventNumber) {
		this.LogPosition = LogPosition;
		this.CommitPosition = CommitPosition ?? LogPosition;
		this.EventNumber = EventNumber;
	}

	public long LogPosition { get; }
	public long CommitPosition { get; }
	public long EventNumber { get; }
}
