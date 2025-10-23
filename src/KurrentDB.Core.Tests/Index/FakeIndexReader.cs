// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Tests.Index;

public class FakeIndexReader : ITransactionFileReader {
	private readonly Func<long, bool> _existsAt;

	public FakeIndexReader(Func<long, bool> existsAt = null) {
		_existsAt = existsAt ?? (l => true);
	}

	public ValueTask<SeqReadResult> TryReadNext<TCursor>(TCursor cursor, CancellationToken token)
		where TCursor : IReadCursor
		=> ValueTask.FromException<SeqReadResult>(new NotImplementedException());

	public ValueTask<SeqReadResult> TryReadPrev<TCursor>(TCursor cursor, CancellationToken token)
		where TCursor : IReadCursor
		=> ValueTask.FromException<SeqReadResult>(new NotImplementedException());

	public ValueTask<RecordReadResult> TryReadAt(long position, bool couldBeScavenged, CancellationToken token) {
		var record = (LogRecord)new PrepareLogRecord(position, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			position.ToString(), null, -1, DateTime.UtcNow, PrepareFlags.None, "type", null,
			Array.Empty<byte>(), null);
		return new(new RecordReadResult(true, position + 1, record, 1));
	}

	public ValueTask<bool> ExistsAt(long position, CancellationToken token)
		=> token.IsCancellationRequested
			? ValueTask.FromCanceled<bool>(token)
			: ValueTask.FromResult(_existsAt(position));
}
