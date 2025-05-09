// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Chunks;

public static class TFConsts {
	public const int MaxLogRecordSize = 32 * 1024 * 1024; // 32Mb ought to be enough for everything?.. ;)
	public const int LogRecordOverheadSize = 10000;
	public const int EffectiveMaxLogRecordSize = MaxLogRecordSize - LogRecordOverheadSize;
	public const int MidpointsDepth = 10;

	public const int ChunkSize = 256 * 1024 * 1024;
	public const int ChunkHeaderSize = 128;
	public const int ChunkFooterSize = 128;
	public const int ChunksCacheSize = 2 * (ChunkSize + ChunkHeaderSize + ChunkFooterSize);
	

	public static TimeSpan MinFlushDelayMs = TimeSpan.FromMilliseconds(2);
}
