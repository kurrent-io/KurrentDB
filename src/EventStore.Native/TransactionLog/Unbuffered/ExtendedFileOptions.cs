// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Unbuffered;

[Flags]
public enum ExtendedFileOptions {
	NoBuffering = unchecked((int)0x20000000),
	Overlapped = unchecked((int)0x40000000),
	SequentialScan = unchecked((int)0x08000000),
	WriteThrough = unchecked((int)0x80000000)
}
