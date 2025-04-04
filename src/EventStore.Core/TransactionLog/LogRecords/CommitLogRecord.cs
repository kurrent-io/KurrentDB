using System;
using System.IO;
using EventStore.Common.Utils;
using EventStore.LogCommon;

namespace EventStore.Core.TransactionLog.LogRecords {
	public class CommitLogRecord : LogRecord, IEquatable<CommitLogRecord> {
		public const byte CommitRecordVersion = 1;

		public readonly long TransactionPosition;
		public readonly long FirstEventNumber;
		public readonly long SortKey;
		public readonly Guid CorrelationId;
		public readonly DateTime TimeStamp;

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

		internal CommitLogRecord(BinaryReader reader, byte version, long logPosition) : base(LogRecordType.Commit,
			version, logPosition) {
			if (version != LogRecordVersion.LogRecordV0 && version != LogRecordVersion.LogRecordV1)
				throw new ArgumentException(
					string.Format("CommitRecord version {0} is incorrect. Supported version: {1}.", version,
						CommitRecordVersion));

			TransactionPosition = reader.ReadInt64();
			FirstEventNumber = version == LogRecordVersion.LogRecordV0 ? reader.ReadInt32() : reader.ReadInt64();

			if (version == LogRecordVersion.LogRecordV0) {
				FirstEventNumber = FirstEventNumber == int.MaxValue ? long.MaxValue : FirstEventNumber;
			}

			SortKey = reader.ReadInt64();
			CorrelationId = new Guid(reader.ReadBytes(16));
			TimeStamp = new DateTime(reader.ReadInt64());
		}

		public override void WriteTo(BinaryWriter writer) {
			base.WriteTo(writer);

			writer.Write(TransactionPosition);
			if (Version == LogRecordVersion.LogRecordV0) {
				int firstEventNumber = FirstEventNumber == long.MaxValue ? int.MaxValue : (int)FirstEventNumber;
				writer.Write(firstEventNumber);
			} else {
				writer.Write(FirstEventNumber);
			}

			writer.Write(SortKey);
			writer.Write(CorrelationId.ToByteArray());
			writer.Write(TimeStamp.Ticks);
		}

		public bool Equals(CommitLogRecord other) {
			if (ReferenceEquals(null, other))
				return false;
			if (ReferenceEquals(this, other))
				return true;
			return other.LogPosition == LogPosition
				   && other.TransactionPosition == TransactionPosition
				   && other.FirstEventNumber == FirstEventNumber
				   && other.SortKey == SortKey
				   && other.CorrelationId == CorrelationId
				   && other.TimeStamp.Equals(TimeStamp);
		}

		public override bool Equals(object obj) {
			if (ReferenceEquals(null, obj))
				return false;
			if (ReferenceEquals(this, obj))
				return true;
			if (obj.GetType() != typeof(CommitLogRecord))
				return false;
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
}
