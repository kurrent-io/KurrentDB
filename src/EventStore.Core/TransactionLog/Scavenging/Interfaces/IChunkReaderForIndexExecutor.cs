// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using DotNext;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IChunkReaderForIndexExecutor<TStreamId> {
	ValueTask<Optional<TStreamId>> TryGetStreamId(long position, CancellationToken token);
}
