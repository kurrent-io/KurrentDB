// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.LogV3;

public struct ReadOnlyMemorySlicer<T> {
	public ReadOnlyMemory<T> Remaining { get; private set; }

	public ReadOnlyMemorySlicer(ReadOnlyMemory<T> source) {
		Remaining = source;
	}

	public ReadOnlyMemory<T> Slice(int length) {
		var toReturn = Remaining[..length];
		Remaining = Remaining[length..];
		return toReturn;
	}
}
