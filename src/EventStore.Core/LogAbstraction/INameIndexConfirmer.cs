// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.LogAbstraction;

public interface INameIndexConfirmer<TValue> : IDisposable {
	ValueTask InitializeWithConfirmed(INameLookup<TValue> source, CancellationToken token);

	/// Entries are confirmed once they are replicated.
	/// Once confirmed, the entry can be persisted.
	// trying quite hard not to use the word 'commit' since it has other uses.
	void Confirm(
		IList<IPrepareLogRecord<TValue>> replicatedPrepares,
		bool catchingUp,
		IIndexBackend<TValue> backend);

	void Confirm(
		IList<IPrepareLogRecord<TValue>> replicatedPrepares,
		CommitLogRecord commit,
		bool catchingUp,
		IIndexBackend<TValue> backend);
}
