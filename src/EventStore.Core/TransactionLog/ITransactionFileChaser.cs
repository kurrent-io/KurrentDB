// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.TransactionLog;

public interface ITransactionFileChaser : IDisposable {
	ICheckpoint Checkpoint { get; }

	void Open();

	ValueTask<SeqReadResult> TryReadNext(CancellationToken token);

	void Close();
	void Flush();
}
