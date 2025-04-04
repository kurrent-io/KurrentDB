// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

public interface IProbabilisticFilter<in TItem> {
	void Add(TItem item);
	bool MightContain(TItem item);
}

public interface IProbabilisticFilter {
	void Add(ReadOnlySpan<byte> item);
	bool MightContain(ReadOnlySpan<byte> item);
}
