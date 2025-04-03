// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Checkpoint;

public interface ICheckpoint : IReadOnlyCheckpoint {
	void Write(long checkpoint);
	void Flush();
	void Close(bool flush);
	IReadOnlyCheckpoint AsReadOnly() => this;
}

public interface IReadOnlyCheckpoint {
	string Name { get; }
	long Read();
	long ReadNonFlushed();

	event Action<long> Flushed;
}
