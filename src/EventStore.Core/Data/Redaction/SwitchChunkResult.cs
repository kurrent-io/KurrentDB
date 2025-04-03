// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data.Redaction;

public enum SwitchChunkResult {
	None = 0,
	Success = 1,
	TargetChunkFileNameInvalid = 2,
	TargetChunkFileNotFound = 3,
	TargetChunkExcessive = 4,
	TargetChunkInactive = 5,
	TargetChunkNotCompleted = 6,
	NewChunkFileNameInvalid = 7,
	NewChunkFileNotFound = 8,
	NewChunkNotCompleted = 9,
	NewChunkHeaderOrFooterInvalid = 10,
	NewChunkHashInvalid = 11,
	NewChunkOpenFailed = 12,
	ChunkRangeDoesNotMatch = 13,
	UnexpectedError = 14,
	TargetChunkFormatNotSupported = 15,
}

public static class SwitchChunkResultExtensions {
	public static string GetErrorMessage(this SwitchChunkResult result) {
		return result switch {
			SwitchChunkResult.TargetChunkFileNameInvalid => "The target chunk's file name is not valid.",
			SwitchChunkResult.TargetChunkFileNotFound => "The target chunk file was not found in the database directory.",
			SwitchChunkResult.TargetChunkExcessive => "The target chunk file is not part of the database.",
			SwitchChunkResult.TargetChunkInactive => "The target chunk file is not actively used by the database.",
			SwitchChunkResult.TargetChunkNotCompleted => "The target chunk is not a completed chunk.",
			SwitchChunkResult.NewChunkFileNameInvalid => "The new chunk's file name is not valid.",
			SwitchChunkResult.NewChunkFileNotFound => "The new chunk file was not found in the database directory.",
			SwitchChunkResult.NewChunkNotCompleted => "The new chunk is not a completed chunk.",
			SwitchChunkResult.NewChunkHeaderOrFooterInvalid => "The new chunk's header or footer is not valid.",
			SwitchChunkResult.NewChunkHashInvalid => "The new chunk has failed hash verification.",
			SwitchChunkResult.NewChunkOpenFailed => "An error has occurred when opening the new chunk.",
			SwitchChunkResult.ChunkRangeDoesNotMatch => "The target chunk's range and the new chunk's range do not match.",
			SwitchChunkResult.UnexpectedError => "An unexpected error has occurred.",
			SwitchChunkResult.TargetChunkFormatNotSupported => "The target chunk's file format is not supported.",
			_ => result.ToString()
		};
	}
}
