// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;

namespace KurrentDB.Core.TransactionLog;

public interface ITransactionFileReader {
	ValueTask<SeqReadResult> TryReadNext<TCursor>(TCursor cursor, CancellationToken token)
		where TCursor : IReadCursor;

	ValueTask<SeqReadResult> TryReadPrev<TCursor>(TCursor cursor, CancellationToken token)
		where TCursor : IReadCursor;

	ValueTask<RecordReadResult> TryReadAt(long position, bool couldBeScavenged, CancellationToken token);

	ValueTask<bool> ExistsAt(long position, CancellationToken token);
}
