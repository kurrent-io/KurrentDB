// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.Index;

public readonly struct IndexKey<TStreamId> {
	public readonly TStreamId StreamId;
	public readonly long Version;
	public readonly long Position;
	public readonly ulong Hash;

	public IndexKey(TStreamId streamId, long version, long position) : this(streamId, version, position, 0) {
	}

	public IndexKey(TStreamId streamId, long version, long position, ulong hash) {
		StreamId = streamId;
		Version = version;
		Position = position;

		Hash = hash;
	}
}
