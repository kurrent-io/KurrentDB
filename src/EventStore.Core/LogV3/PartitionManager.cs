using System;
using System.IO;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.LogV3 {
	public class PartitionManager : IPartitionManager {
		private static readonly ILogger _log = Serilog.Log.ForContext<PartitionManager>();
		private readonly ITransactionFileReader _reader;
		private readonly ITransactionFileWriter _writer;
		private readonly LogV3RecordFactory _recordFactory;

		private const string RootPartitionName = "Root";
		private const string RootPartitionTypeName = "Root";

		public Guid? RootId { get; private set; }
		public Guid? RootTypeId { get; private set; }

		public PartitionManager(
			ITransactionFileReader reader, ITransactionFileWriter writer, LogV3RecordFactory recordFactory) {
			_reader = reader;
			_writer = writer;
			_recordFactory = recordFactory;
		}

		public void Initialize() {
			if (RootId.HasValue)
				return;

			ReadRootPartition();

			EnsureRootPartitionIsWritten();
		}

		private void EnsureRootPartitionIsWritten() {
			// below code only takes into account offline truncation
			if (!RootTypeId.HasValue) {
				RootTypeId = Guid.NewGuid();
				long pos = _writer.Position;
				var rootPartitionType = _recordFactory.CreatePartitionTypeRecord(
					timeStamp: DateTime.UtcNow,
					logPosition: pos,
					partitionTypeId: RootTypeId.Value,
					partitionId: Guid.Empty,
					name: RootPartitionTypeName);

				if (!_writer.Write(rootPartitionType, out pos))
					throw new Exception($"Failed to write root partition type!");

				_writer.Flush();

				_log.Debug("Root partition type created, id: {id}", RootTypeId);
			}

			if (!RootId.HasValue) {
				RootId = Guid.NewGuid();
				long pos = _writer.Position;
				var rootPartition = _recordFactory.CreatePartitionRecord(
					timeStamp: DateTime.UtcNow,
					logPosition: pos,
					partitionId: RootId.Value,
					partitionTypeId: RootTypeId.Value,
					parentPartitionId: Guid.Empty,
					flags: 0,
					referenceNumber: 0,
					name: RootPartitionName);

				if (!_writer.Write(rootPartition, out pos))
					throw new Exception($"Failed to write root partition!");

				_writer.Flush();

				_recordFactory.SetRootPartitionId(RootId.Value);

				_log.Debug("Root partition created, id: {id}", RootId);
			}
		}

		private void ReadRootPartition() {
			SeqReadResult result;
			_reader.Reposition(0);
			while ((result = _reader.TryReadNext()).Success) {
				var rec = result.LogRecord;
				switch (rec.RecordType) {
					case LogRecordType.PartitionType:
						var r = ((PartitionTypeLogRecord)rec).Record;
						if (r.StringPayload == RootPartitionTypeName && r.SubHeader.PartitionId == Guid.Empty) {
							RootTypeId = r.Header.RecordId;

							_log.Debug("Root partition type read, id: {id}", RootTypeId);

							break;
						}

						throw new InvalidDataException(
							"Unexpected partition type encountered while trying to read the root partition type.");

					case LogRecordType.Partition:
						var p = ((PartitionLogRecord)rec).Record;
						if (p.StringPayload == RootPartitionName && p.SubHeader.PartitionTypeId == RootTypeId
																 && p.SubHeader.ParentPartitionId == Guid.Empty) {
							RootId = p.Header.RecordId;
							_recordFactory.SetRootPartitionId(RootId.Value);

							_log.Debug("Root partition read, id: {id}", RootId);

							return;
						}

						throw new InvalidDataException(
							"Unexpected partition encountered while trying to read the root partition.");

					case LogRecordType.System:
						var systemLogRecord = (ISystemLogRecord)result.LogRecord;
						if (systemLogRecord.SystemRecordType == SystemRecordType.Epoch) {
							continue;
						}

						throw new ArgumentOutOfRangeException("SystemRecordType",
							"Unexpected system record while trying to read the root partition");

					default:
						throw new ArgumentOutOfRangeException("RecordType",
							"Unexpected record while trying to read the root partition");
				}
			}
		}
	}
}
