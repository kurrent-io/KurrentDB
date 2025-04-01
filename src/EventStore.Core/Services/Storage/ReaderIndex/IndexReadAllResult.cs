// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;

namespace EventStore.Core.Services.Storage.ReaderIndex;

public readonly struct IndexReadAllResult(
	List<CommitEventRecord> records,
	TFPos currentPos,
	TFPos nextPos,
	TFPos prevPos,
	bool isEndOfStream,
	long consideredEventsCount) {
	public readonly List<CommitEventRecord> Records = records;
	public readonly TFPos CurrentPos = currentPos;
	public readonly TFPos NextPos = nextPos;
	public readonly TFPos PrevPos = prevPos;
	public readonly bool IsEndOfStream = isEndOfStream;
	public readonly long ConsideredEventsCount = consideredEventsCount;

	public override string ToString() =>
		$"CurrentPos: {CurrentPos}, NextPos: {NextPos}, PrevPos: {PrevPos}, " +
		$"IsEndOfStream: {IsEndOfStream}, Records: {string.Join("\n", Records.Select(x => x.ToString()))}";
}
