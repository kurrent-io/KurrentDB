// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data;

public struct Range {
	public readonly long Lower;
	public readonly long Upper;

	public Range(long lower, long upper) {
		Lower = lower;
		Upper = upper;
	}
}
