// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

public struct BulkReadResult {
	public readonly int OldPosition;
	public readonly int BytesRead;
	public readonly bool IsEOF;

	public BulkReadResult(int oldPosition, int bytesRead, bool isEof) {
		OldPosition = oldPosition;
		BytesRead = bytesRead;
		IsEOF = isEof;
	}
}
