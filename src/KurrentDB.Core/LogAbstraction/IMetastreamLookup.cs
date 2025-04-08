// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.LogAbstraction;

public interface IMetastreamLookup<TStreamId> {
	bool IsMetaStream(TStreamId streamId);
	TStreamId MetaStreamOf(TStreamId streamId);
	TStreamId OriginalStreamOf(TStreamId streamId);
}
