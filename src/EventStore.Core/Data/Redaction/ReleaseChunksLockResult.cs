// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data.Redaction;

public enum ReleaseChunksLockResult {
	None = 0,
	Success = 1,
	Failed = 2
}

public static class ReleaseChunksLockResultExtensions {
	public static string GetErrorMessage(this ReleaseChunksLockResult result) {
		return result switch {
			ReleaseChunksLockResult.Failed => "Failed to release lock.",
			_ => result.ToString()
		};
	}
}
