// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class AdHocIndexReaderInterceptor<TStreamId> : IIndexReaderForCalculator<TStreamId> {
	private readonly IIndexReaderForCalculator<TStreamId> _wrapped;
	private readonly Func<
		Func<StreamHandle<TStreamId>, long, int, ScavengePoint, CancellationToken, ValueTask<IndexReadEventInfoResult>>,
		StreamHandle<TStreamId>, long, int, ScavengePoint, CancellationToken, ValueTask<IndexReadEventInfoResult>> _f;


	public AdHocIndexReaderInterceptor(
		IIndexReaderForCalculator<TStreamId> wrapped,
		Func<
			Func<StreamHandle<TStreamId>, long, int, ScavengePoint, CancellationToken, ValueTask<IndexReadEventInfoResult>>,
			StreamHandle<TStreamId>, long, int, ScavengePoint, CancellationToken, ValueTask<IndexReadEventInfoResult>> f) {

		_wrapped = wrapped;
		_f = f;
	}

	public ValueTask<long> GetLastEventNumber(
		StreamHandle<TStreamId> streamHandle,
		ScavengePoint scavengePoint,
		CancellationToken token) {

		return _wrapped.GetLastEventNumber(streamHandle, scavengePoint, token);
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward(
		StreamHandle<TStreamId> stream,
		long fromEventNumber,
		int maxCount,
		ScavengePoint scavengePoint,
		CancellationToken token) {

		return _f(_wrapped.ReadEventInfoForward, stream, fromEventNumber, maxCount, scavengePoint, token);
	}

	public ValueTask<bool> IsTombstone(long logPosition, CancellationToken token) {
		return _wrapped.IsTombstone(logPosition, token);
	}
}
