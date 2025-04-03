// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class AdHocIndexScavengerInterceptor : IIndexScavenger {
	private readonly IIndexScavenger _wrapped;
	private readonly Func<Func<IndexEntry, CancellationToken, ValueTask<bool>>, Func<IndexEntry, CancellationToken, ValueTask<bool>>> _f;

	public AdHocIndexScavengerInterceptor(
		IIndexScavenger wrapped,
		Func<Func<IndexEntry, CancellationToken, ValueTask<bool>>, Func<IndexEntry, CancellationToken, ValueTask<bool>>> f) {

		_wrapped = wrapped;
		_f = f;
	}

	public ValueTask ScavengeIndex(
		long scavengePoint,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep,
		IIndexScavengerLog log,
		CancellationToken cancellationToken) {

		return _wrapped.ScavengeIndex(scavengePoint,_f(shouldKeep), log, cancellationToken);
	}
}
