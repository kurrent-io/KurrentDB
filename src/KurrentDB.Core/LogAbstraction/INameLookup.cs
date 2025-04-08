// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using DotNext;

namespace EventStore.Core.LogAbstraction;

/// Looks up a name given a value
public interface INameLookup<TValue> {
	/// returns false if there is no max (i.e. the source is empty)
	ValueTask<Optional<TValue>> TryGetLastValue(CancellationToken token);

	ValueTask<string> LookupName(TValue value, CancellationToken token);
}
