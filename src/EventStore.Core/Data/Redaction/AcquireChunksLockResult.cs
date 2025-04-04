// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data.Redaction;

public enum AcquireChunksLockResult {
	None = 0,
	Success = 1,
	Failed = 2
}

public static class AcquireChunksLockResultExtensions {
	public static string GetErrorMessage(this AcquireChunksLockResult result) {
		return result switch {
			AcquireChunksLockResult.Failed => "Failed to acquire lock.",
			_ => result.ToString()
		};
	}
}
