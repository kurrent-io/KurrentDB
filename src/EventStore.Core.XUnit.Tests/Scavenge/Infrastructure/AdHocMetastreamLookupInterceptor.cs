// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class AdHocMetastreamLookupInterceptor<TStreamId> : IMetastreamLookup<TStreamId> {
	private readonly IMetastreamLookup<TStreamId> _wrapped;
	private readonly Func<Func<TStreamId, bool>, TStreamId, bool> _f;

	public AdHocMetastreamLookupInterceptor(
		IMetastreamLookup<TStreamId> wrapped,
		Func<Func<TStreamId, bool>, TStreamId, bool> f) {

		_wrapped = wrapped;
		_f = f;
	}

	public bool IsMetaStream(TStreamId streamId) =>
		_f(_wrapped.IsMetaStream, streamId);

	public TStreamId MetaStreamOf(TStreamId streamId) =>
		_wrapped.MetaStreamOf(streamId);

	public TStreamId OriginalStreamOf(TStreamId streamId) =>
		_wrapped.OriginalStreamOf(streamId);
}
