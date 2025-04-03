// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Chunks;

public static class TFConsts {
	public const int MaxLogRecordSize = 16 * 1024 * 1024; // 16Mb ought to be enough for everything?.. ;)
	public const int LogRecordOverheadSize = 10000;
	public const int EffectiveMaxLogRecordSize = MaxLogRecordSize - LogRecordOverheadSize;
	public const int MidpointsDepth = 10;

	public const int ChunkSize = 256 * 1024 * 1024;
	public const int ChunkHeaderSize = 128;
	public const int ChunkFooterSize = 128;
	public const int ChunksCacheSize = 2 * (ChunkSize + ChunkHeaderSize + ChunkFooterSize);
	

	public static TimeSpan MinFlushDelayMs = TimeSpan.FromMilliseconds(2);
}
