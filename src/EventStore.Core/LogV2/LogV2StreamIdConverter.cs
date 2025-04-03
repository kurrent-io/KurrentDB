// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.LogV2;

public class LogV2StreamIdConverter : IStreamIdConverter<string> {
	public string ToStreamId(ReadOnlySpan<byte> bytes) {
		unsafe {
			fixed (byte* b = bytes) {
				return Encoding.UTF8.GetString(b, bytes.Length);
			}
		}
	}

	public string ToStreamId(ReadOnlyMemory<byte> bytes) =>
		ToStreamId(bytes.Span);
}
