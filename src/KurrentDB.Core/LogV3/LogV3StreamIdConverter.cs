// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.LogAbstraction;
using StreamId = System.UInt32;

namespace EventStore.Core.LogV3;

public class LogV3StreamIdConverter : IStreamIdConverter<StreamId> {
	public uint ToStreamId(ReadOnlySpan<byte> bytes) {
		throw new NotImplementedException();
	}

	public uint ToStreamId(ReadOnlyMemory<byte> bytes) {
		throw new NotImplementedException();
	}
}
