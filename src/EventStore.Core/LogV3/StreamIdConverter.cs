// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using StreamId = System.UInt32;

namespace EventStore.Core.LogV3;

/// Converts between StreamIds and their event number in the streams stream.
public static class StreamIdConverter {
	static readonly StreamId _offset = LogV3SystemStreams.FirstRealStream / LogV3SystemStreams.StreamInterval;

	public static StreamId ToStreamId(long index) {
		return ((StreamId)index + _offset) * LogV3SystemStreams.StreamInterval;
	}

	public static long ToEventNumber(StreamId streamId) {
		if (streamId % LogV3SystemStreams.StreamInterval != 0)
			throw new ArgumentOutOfRangeException(nameof(streamId), "streamId must be even");

		return streamId / LogV3SystemStreams.StreamInterval - _offset;
	}
}
