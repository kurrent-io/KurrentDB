// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.Scavenging.Data;

namespace KurrentDB.Core.TransactionLog.Scavenging.Interfaces;

public interface IIndexReaderForAccumulator<TStreamId> {
	ValueTask<IndexReadEventInfoResult> ReadEventInfoForward(
		StreamHandle<TStreamId> handle,
		long fromEventNumber,
		int maxCount,
		ScavengePoint scavengePoint,
		CancellationToken token);

	ValueTask<IndexReadEventInfoResult> ReadEventInfoBackward(
		TStreamId streamId,
		StreamHandle<TStreamId> handle,
		long fromEventNumber,
		int maxCount,
		ScavengePoint scavengePoint,
		CancellationToken token);
}
