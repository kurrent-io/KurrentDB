// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

public class CorruptedHashException : Exception {
	public CorruptedHashException(int rebuildCount, string error) : base(error) {
		RebuildCount = rebuildCount;
	}

	public int RebuildCount { get; }
}
