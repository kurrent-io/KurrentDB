// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;

namespace EventStore.Core.TransactionLog.Chunks;

public interface IChunkRawReader : IDisposable {
	Stream Stream { get; }
}
