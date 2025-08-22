// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Time;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.TransactionLog;

public interface ITransactionFileTracker {
	void OnRead(Instant start, ILogRecord record, Source source);

	//qq not sure if this should be different to the limiter (re)source
	enum Source {
		Unknown,
		Archive,
		//qq Archive Cache? or is that just filesystem/chunkcache accordingly
		ChunkCache,
		FileSystem,
	};
}
