// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog;
using YamlDotNet.Core;

namespace KurrentDB.Core.Tests.Fakes;

public class FakeTfReader : ITransactionFileReader {
	public ValueTask<SeqReadResult> TryReadNext<TCursor>(TCursor cursor, CancellationToken token)
		where TCursor : IReadCursor
		=> ValueTask.FromException<SeqReadResult>(new NotImplementedException());

	public ValueTask<SeqReadResult> TryReadPrev<TCursor>(TCursor cursor, CancellationToken token)
		where TCursor : IReadCursor
		=> ValueTask.FromException<SeqReadResult>(new NotImplementedException());

	public ValueTask<RecordReadResult> TryReadAt(long position, bool couldBeScavenged, CancellationToken token)
		=> ValueTask.FromException<RecordReadResult>(new NotImplementedException());

	public ValueTask<bool> ExistsAt(long position, CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled<bool>(token) : ValueTask.FromResult(true);
}
