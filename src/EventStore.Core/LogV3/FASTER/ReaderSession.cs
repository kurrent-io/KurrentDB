// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using FASTER.core;

namespace EventStore.Core.LogV3.FASTER;

internal class ReaderSession<TValue> : IDisposable {
	public ReaderSession(
		ClientSession<SpanByte, TValue, TValue, TValue, Context<TValue>, ReaderFunctions<TValue>> clientSession,
		Context<TValue> context) {
		ClientSession = clientSession;
		Context = context;
	}

	public ClientSession<SpanByte, TValue, TValue, TValue, Context<TValue>, ReaderFunctions<TValue>> ClientSession { get; }
	public Context<TValue> Context { get; }

	public void Dispose() {
		ClientSession?.Dispose();
	}
}

