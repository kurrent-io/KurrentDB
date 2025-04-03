// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using FASTER.core;

namespace EventStore.Core.LogV3.FASTER;

public struct WriterFunctions<TValue> : IFunctions<SpanByte, TValue> {
	public bool SupportsLocking => false;

	public void SingleWriter(ref SpanByte key, ref TValue src, ref TValue dst) {
		dst = src;
	}

	public bool ConcurrentWriter(ref SpanByte key, ref TValue src, ref TValue dst) {
		dst = src;
		return true;
	}

	public void UpsertCompletionCallback(ref SpanByte key, ref TValue value, Empty context) {
	}

	public void CheckpointCompletionCallback(string sessionId, CommitPoint commitPoint) {
		if (commitPoint.ExcludedSerialNos.Count != 0)
			throw new Exception($"{commitPoint.ExcludedSerialNos.Count} writes unexpectedly excluded from checkpoint");
	}

	#region not needed
	public void SingleReader(ref SpanByte key, ref TValue input, ref TValue value, ref TValue dst) =>
		throw new NotImplementedException();

	public void ConcurrentReader(ref SpanByte key, ref TValue input, ref TValue value, ref TValue dst) =>
		throw new NotImplementedException();

	public void ReadCompletionCallback(ref SpanByte key, ref TValue input, ref TValue output, Empty context, Status status) =>
		throw new NotImplementedException();

	public void CopyUpdater(ref SpanByte key, ref TValue input, ref TValue oldValue, ref TValue newValue, ref TValue output) =>
		throw new NotImplementedException();

	public void InitialUpdater(ref SpanByte key, ref TValue input, ref TValue value, ref TValue output) =>
		throw new NotImplementedException();

	public bool InPlaceUpdater(ref SpanByte key, ref TValue input, ref TValue value, ref TValue output) =>
		throw new NotImplementedException();

	public void RMWCompletionCallback(ref SpanByte key, ref TValue input, ref TValue output, Empty ctx, Status status) =>
		throw new NotImplementedException();

	public void DeleteCompletionCallback(ref SpanByte key, Empty context) =>
		throw new NotImplementedException();

	public void Lock(ref RecordInfo recordInfo, ref SpanByte key, ref TValue value, LockType lockType, ref long lockContext) =>
		throw new NotImplementedException();

	public bool Unlock(ref RecordInfo recordInfo, ref SpanByte key, ref TValue value, LockType lockType, long lockContext) =>
		throw new NotImplementedException();
	#endregion
}

