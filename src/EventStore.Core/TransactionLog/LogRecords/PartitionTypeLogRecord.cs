using System;
using EventStore.LogV3;

namespace EventStore.Core.TransactionLog.LogRecords {
	public class PartitionTypeLogRecord : LogV3Record<StringPayloadRecord<Raw.PartitionTypeHeader>> {
		public PartitionTypeLogRecord(DateTime timeStamp, long logPosition, Guid partitionTypeId, Guid partitionId,
			string name) {

			Record = RecordCreator.CreatePartitionTypeRecord(
				timeStamp,
				logPosition,
				partitionTypeId,
				partitionId,
				name);
		}

		public PartitionTypeLogRecord(ReadOnlyMemory<byte> bytes) : base() {
			Record = StringPayloadRecord.Create(new RecordView<Raw.PartitionTypeHeader>(bytes));
		}
	}
}
