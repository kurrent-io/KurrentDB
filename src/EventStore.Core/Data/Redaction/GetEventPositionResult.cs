// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data.Redaction;

public enum GetEventPositionResult {
	None = 0,
	Success = 1,
	UnexpectedError = 2
}

public static class GetEventPositionResultExtensions {
	public static string GetErrorMessage(this GetEventPositionResult result) {
		return result switch {
			GetEventPositionResult.UnexpectedError => "An unexpected error has occurred.",
			_ => result.ToString()
		};
	}
}
