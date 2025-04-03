// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using DotNext.Buffers;
using DotNext.Buffers.Binary;
using DotNext.IO;
using EventStore.Common.Utils;
using EventStore.LogCommon;

namespace EventStore.Core.TransactionLog.LogRecords;

public sealed class CommitLogRecord : LogRecord, IEquatable<CommitLogRecord> {
	public const byte CommitRecordVersion = 1;

	public long TransactionPosition { get; }
	public long FirstEventNumber { get; }
	public long SortKey { get; }
	public Guid CorrelationId { get; }
	public DateTime TimeStamp { get; }

	public CommitLogRecord(long logPosition,
		Guid correlationId,
		long transactionPosition,
		DateTime timeStamp,
		long firstEventNumber,
		byte commitRecordVersion = CommitRecordVersion)
		: base(LogRecordType.Commit, commitRecordVersion, logPosition) {
		Ensure.NotEmptyGuid(correlationId, "correlationId");
		Ensure.Nonnegative(transactionPosition, "TransactionPosition");
		Ensure.Nonnegative(firstEventNumber, "eventNumber");

		TransactionPosition = transactionPosition;
		FirstEventNumber = firstEventNumber;
		SortKey = logPosition;
		CorrelationId = correlationId;
		TimeStamp = timeStamp;
	}

	internal CommitLogRecord(ref SequenceReader reader, byte version, long logPosition)
		: base(LogRecordType.Commit, version, logPosition) {
		if (version is not LogRecordVersion.LogRecordV0 and not LogRecordVersion.LogRecordV1)
			throw new ArgumentException(
				$"CommitRecord version {version} is incorrect. Supported version: {CommitRecordVersion}.");

		TransactionPosition = reader.ReadLittleEndian<long>();
		FirstEventNumber = version is LogRecordVersion.LogRecordV0
			? AdjustVersion(reader.ReadLittleEndian<int>())
			: reader.ReadLittleEndian<long>();
		SortKey = reader.ReadLittleEndian<long>();
		CorrelationId = reader.Read<Blittable<Guid>>().Value;
		TimeStamp = new(reader.ReadLittleEndian<long>());

		static long AdjustVersion(int version)
			=> version is int.MaxValue ? long.MaxValue : version;
	}

	public override void WriteTo(ref BufferWriterSlim<byte> writer) {
		base.WriteTo(ref writer);

		writer.WriteLittleEndian(TransactionPosition);
		if (Version is LogRecordVersion.LogRecordV0) {
			int firstEventNumber = FirstEventNumber is long.MaxValue ? int.MaxValue : (int)FirstEventNumber;
			writer.WriteLittleEndian(firstEventNumber);
		} else {
			writer.WriteLittleEndian(FirstEventNumber);
		}

		writer.WriteLittleEndian(SortKey);

		Span<byte> correlationIdBuffer = writer.GetSpan(16);
		CorrelationId.TryWriteBytes(correlationIdBuffer);
		writer.Advance(16);

		writer.WriteLittleEndian(TimeStamp.Ticks);
	}

	public override int GetSizeWithLengthPrefixAndSuffix() {
		return sizeof(int) * 2														/* Length prefix & suffix */
		+ sizeof(long)																/* TransactionPosition */
		+ (Version is LogRecordVersion.LogRecordV0 ? sizeof(int) : sizeof(long))	/* Version */
		+ sizeof(long)																/* SortKey */
		+ 16																		/* CorrelationId */
		+ sizeof(long)																/* TimeStamp */
		+ BaseSize;
	}

	public bool Equals(CommitLogRecord other) {
		if (ReferenceEquals(null, other)) return false;
		if (ReferenceEquals(this, other)) return true;
		return other.LogPosition == LogPosition
		       && other.TransactionPosition == TransactionPosition
		       && other.FirstEventNumber == FirstEventNumber
		       && other.SortKey == SortKey
		       && other.CorrelationId == CorrelationId
		       && other.TimeStamp.Equals(TimeStamp);
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj)) return false;
		if (ReferenceEquals(this, obj)) return true;
		if (obj.GetType() != typeof(CommitLogRecord)) return false;
		return Equals((CommitLogRecord)obj);
	}

	public override int GetHashCode() {
		unchecked {
			int result = LogPosition.GetHashCode();
			result = (result * 397) ^ TransactionPosition.GetHashCode();
			result = (result * 397) ^ FirstEventNumber.GetHashCode();
			result = (result * 397) ^ SortKey.GetHashCode();
			result = (result * 397) ^ CorrelationId.GetHashCode();
			result = (result * 397) ^ TimeStamp.GetHashCode();
			return result;
		}
	}

	public static bool operator ==(CommitLogRecord left, CommitLogRecord right) {
		return Equals(left, right);
	}

	public static bool operator !=(CommitLogRecord left, CommitLogRecord right) {
		return !Equals(left, right);
	}

	public override string ToString() {
		return string.Format("LogPosition: {0}, "
		                     + "TransactionPosition: {1}, "
		                     + "FirstEventNumber: {2}, "
		                     + "SortKey: {3}, "
		                     + "CorrelationId: {4}, "
		                     + "TimeStamp: {5}",
			LogPosition,
			TransactionPosition,
			FirstEventNumber,
			SortKey,
			CorrelationId,
			TimeStamp);
	}
}
