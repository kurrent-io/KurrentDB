// Copyright (c) Kurrent, Inc and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Storage;


internal struct AllRecord {
	public long Seq { get; set; }
	public long LogPosition { get; set; }
	public int EventNumber { get; set; }
}

public record struct ReferenceRecord(long Id, string Name);
