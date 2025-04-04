// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using FASTER.core;

namespace EventStore.Core.LogV3.FASTER;

// for deleting and checkpointing
public struct MaintenanceFunctions<TValue> : IFunctions<SpanByte, TValue> {
	public bool SupportsLocking => false;

	public void DeleteCompletionCallback(ref SpanByte key, Empty ctx) {
	}

	public void CheckpointCompletionCallback(string sessionId, CommitPoint commitPoint) {
	}

	#region not needed
	public void ConcurrentReader(ref SpanByte key, ref TValue input, ref TValue value, ref TValue dst) =>
		throw new NotImplementedException();

	public bool ConcurrentWriter(ref SpanByte key, ref TValue src, ref TValue dst) =>
		throw new NotImplementedException();

	public void CopyUpdater(ref SpanByte key, ref TValue input, ref TValue oldValue, ref TValue newValue, ref TValue output) =>
		throw new NotImplementedException();

	public void InitialUpdater(ref SpanByte key, ref TValue input, ref TValue value, ref TValue output) =>
		throw new NotImplementedException();

	public bool InPlaceUpdater(ref SpanByte key, ref TValue input, ref TValue value, ref TValue output) =>
		throw new NotImplementedException();

	public void Lock(ref RecordInfo recordInfo, ref SpanByte key, ref TValue value, LockType lockType, ref long lockContext) =>
		throw new NotImplementedException();

	public void ReadCompletionCallback(ref SpanByte key, ref TValue input, ref TValue output, Empty ctx, Status status) =>
		throw new NotImplementedException();

	public void RMWCompletionCallback(ref SpanByte key, ref TValue input, ref TValue output, Empty ctx, Status status) =>
		throw new NotImplementedException();

	public void SingleReader(ref SpanByte key, ref TValue input, ref TValue value, ref TValue dst) =>
		throw new NotImplementedException();

	public void SingleWriter(ref SpanByte key, ref TValue src, ref TValue dst) =>
		throw new NotImplementedException();

	public bool Unlock(ref RecordInfo recordInfo, ref SpanByte key, ref TValue value, LockType lockType, long lockContext) =>
		throw new NotImplementedException();

	public void UpsertCompletionCallback(ref SpanByte key, ref TValue value, Empty ctx) =>
		throw new NotImplementedException();
	#endregion
}

