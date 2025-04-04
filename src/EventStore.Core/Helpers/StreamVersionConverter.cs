// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
