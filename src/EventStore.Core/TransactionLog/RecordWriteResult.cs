// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog;

public struct RecordWriteResult {
	public bool Success;
	public readonly long OldPosition;
	public readonly long NewPosition;

	public RecordWriteResult(bool success, long oldPosition, long newPosition) {
		if (newPosition < oldPosition)
			throw new ArgumentException("New position is less than old position.");

		Success = success;
		OldPosition = oldPosition;
		NewPosition = newPosition;
	}

	public static RecordWriteResult Failed(long position) {
		return new RecordWriteResult(false, position, position);
	}

	public static RecordWriteResult Successful(long oldPosition, long newPosition) {
		return new RecordWriteResult(true, oldPosition, newPosition);
	}

	public override string ToString() {
		return string.Format("Success: {0}, OldPosition: {1}, NewPosition: {2}", Success, OldPosition, NewPosition);
	}
}
