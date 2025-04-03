// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Helpers;

public static class StreamVersionConverter {
	public static int Downgrade(long expectedVersion) {
		if (expectedVersion == long.MaxValue) {
			return int.MaxValue;
		}

		return (int)expectedVersion;
	}

	public static long Upgrade(long expectedVersion) {
		return expectedVersion == int.MaxValue ? long.MaxValue : expectedVersion;
	}
}
