// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.DbAccess;

public class IndexReaderForCalculator<TStreamId> : IIndexReaderForCalculator<TStreamId> {
	private readonly IReadIndex<TStreamId> _readIndex;
	private readonly Func<TFReaderLease> _tfReaderFactory;
	private readonly Func<ulong, TStreamId> _lookupUniqueHashUser;

	public IndexReaderForCalculator(
		IReadIndex<TStreamId> readIndex,
		Func<TFReaderLease> tfReaderFactory,
		Func<ulong, TStreamId> lookupUniqueHashUser) {

		_readIndex = readIndex;
		_tfReaderFactory = tfReaderFactory;
		_lookupUniqueHashUser = lookupUniqueHashUser;
	}

	public ValueTask<long> GetLastEventNumber(
		StreamHandle<TStreamId> handle,
		ScavengePoint scavengePoint,
		CancellationToken token) {

		return handle.Kind switch {
			StreamHandle.Kind.Hash =>
				// tries as far as possible to use the index without consulting the log to fetch the last event number
				_readIndex.GetStreamLastEventNumber_NoCollisions(handle.StreamHash, _lookupUniqueHashUser,
					scavengePoint.Position, token),
			StreamHandle.Kind.Id =>
				// uses the index and the log to fetch the last event number
				_readIndex.GetStreamLastEventNumber_KnownCollisions(handle.StreamId, scavengePoint.Position, token),
			_ => ValueTask.FromException<long>(new ArgumentOutOfRangeException(nameof(handle), handle, null))
		};
	}

	public ValueTask<IndexReadEventInfoResult> ReadEventInfoForward(
		StreamHandle<TStreamId> handle,
		long fromEventNumber,
		int maxCount,
		ScavengePoint scavengePoint,
		CancellationToken token) {

		return handle.Kind switch {
			StreamHandle.Kind.Hash =>
				// uses the index only
				_readIndex.ReadEventInfoForward_NoCollisions(handle.StreamHash, fromEventNumber, maxCount,
					scavengePoint.Position, token),
			StreamHandle.Kind.Id =>
				// uses log to check for hash collisions
				_readIndex.ReadEventInfoForward_KnownCollisions(handle.StreamId, fromEventNumber, maxCount,
					scavengePoint.Position, token),
			_ => ValueTask.FromException<IndexReadEventInfoResult>(
				new ArgumentOutOfRangeException(nameof(handle), handle, null))
		};
	}

	public async ValueTask<bool> IsTombstone(long logPosition, CancellationToken token) {
		using var reader = _tfReaderFactory();
		var result = await reader.TryReadAt(logPosition, couldBeScavenged: true, token);

		if (!result.Success)
			return false;

		if (result.LogRecord is not IPrepareLogRecord prepare)
			throw new Exception(
				$"Incorrect type of log record {result.LogRecord.RecordType}, " +
				$"expected Prepare record.");

		return prepare.Flags.HasAnyOf(PrepareFlags.StreamDelete);
	}
}
