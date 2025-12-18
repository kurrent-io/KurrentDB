// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;
using Jint.Native.Json;
using KurrentDB.Core.Data;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

internal sealed class PositionObject(Engine engine, JsonParser parser) : JsObject(engine, parser) {
	private string StreamId {
		set => SetReadOnlyProperty("streamId", value);
	}

	private ulong StreamRevision {
		set => SetReadOnlyProperty("streamRevision", value);
	}

	private long PartitionId {
		set => SetReadOnlyProperty("partitionId", value);
	}

	private ulong LogPosition {
		set => SetReadOnlyProperty("logPosition", value);
	}

	public void MapFrom(ResolvedEvent resolvedEvent, ulong sequenceId) {
		StreamId = resolvedEvent.OriginalStreamId;
		StreamRevision = Convert.ToUInt64(resolvedEvent.OriginalEventNumber);
		LogPosition = Convert.ToUInt64(resolvedEvent.OriginalPosition!.Value.CommitPosition);
	}
}
