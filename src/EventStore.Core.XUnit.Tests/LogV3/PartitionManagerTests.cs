using System;
using System.Collections.Generic;
using EventStore.Core.LogAbstraction;
using EventStore.Core.LogV3;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogV3 {

	public class PartitionManagerTests {

		[Fact]
		public void creates_new_root_partition_on_first_epoch() {
			var reader = new FakeReader(withoutRecords: true);
			var writer = new FakeWriter();

			IPartitionManager partitionManager = new PartitionManager(reader, writer, new LogV3RecordFactory());

			partitionManager.Initialize();

			Assert.Collection(writer.WrittenRecords,
				r => {
					Assert.Equal(LogRecordType.PartitionType, r.RecordType);
					Assert.IsType<PartitionTypeLogRecord>(r);
					Assert.Equal("Root", ((PartitionTypeLogRecord)r).Record.StringPayload);
					Assert.Equal(partitionManager.RootTypeId, ((PartitionTypeLogRecord)r).Record.Header.RecordId);
					Assert.Equal(Guid.Empty, ((PartitionTypeLogRecord)r).Record.SubHeader.PartitionId);
				},
				r => {
					Assert.Equal(LogRecordType.Partition, r.RecordType);
					Assert.IsType<PartitionLogRecord>(r);
					Assert.Equal("Root", ((PartitionLogRecord)r).Record.StringPayload);
					Assert.Equal(partitionManager.RootId, ((PartitionLogRecord)r).Record.Header.RecordId);
					Assert.Equal(partitionManager.RootTypeId, ((PartitionLogRecord)r).Record.SubHeader.PartitionTypeId);
					Assert.Equal(Guid.Empty, ((PartitionLogRecord)r).Record.SubHeader.ParentPartitionId);
				});

			Assert.True(writer.IsFlushed);
		}

		[Fact]
		public void configures_record_factory_with_root_partition_id() {
			var reader = new FakeReader();
			var recordFactory = new LogV3RecordFactory();

			IPartitionManager partitionManager = new PartitionManager(reader, new FakeWriter(), recordFactory);

			partitionManager.Initialize();

			var streamRecord = (LogV3StreamRecord)recordFactory.CreateStreamRecord(Guid.NewGuid(), 1, DateTime.UtcNow, 1, "stream-1");
			Assert.Equal(partitionManager.RootId, streamRecord.Record.SubHeader.PartitionId);
		}

		[Fact]
		public void reads_root_partition_once_initialized() {
			var rootPartitionId = Guid.NewGuid();
			var rootPartitionTypeId = Guid.NewGuid();
			var reader = new FakeReader(rootPartitionId, rootPartitionTypeId);
			var writer = new FakeWriter();

			IPartitionManager partitionManager = new PartitionManager(reader, writer, new LogV3RecordFactory());

			partitionManager.Initialize();

			Assert.Empty(writer.WrittenRecords);
			Assert.Equal(rootPartitionId, partitionManager.RootId);
			Assert.Equal(rootPartitionTypeId, partitionManager.RootTypeId);
		}

		[Fact]
		public void reads_root_partition_only_once() {
			var reader = new FakeReader();
			var writer = new FakeWriter();

			IPartitionManager partitionManager = new PartitionManager(reader, writer, new LogV3RecordFactory());

			partitionManager.Initialize();
			partitionManager.Initialize();

			Assert.Empty(writer.WrittenRecords);
			Assert.Equal(2, reader.ReadCount);
		}

		[Fact]
		public void creates_root_partition_in_case_it_partially_failed_previously() {
			var rootPartitionTypeId = Guid.NewGuid();
			var reader = new FakeReader(rootPartitionId: null, rootPartitionTypeId);
			var writer = new FakeWriter();

			IPartitionManager partitionManager = new PartitionManager(reader, writer, new LogV3RecordFactory());

			partitionManager.Initialize();

			Assert.True(partitionManager.RootId.HasValue);
			Assert.Equal(rootPartitionTypeId, partitionManager.RootTypeId);
			Assert.Collection(writer.WrittenRecords,
				r => {
					Assert.Equal(LogRecordType.Partition, r.RecordType);
					Assert.IsType<PartitionLogRecord>(r);
					Assert.Equal("Root", ((PartitionLogRecord)r).Record.StringPayload);
					Assert.Equal(partitionManager.RootId, ((PartitionLogRecord)r).Record.Header.RecordId);
					Assert.Equal(partitionManager.RootTypeId, ((PartitionLogRecord)r).Record.SubHeader.PartitionTypeId);
					Assert.Equal(Guid.Empty, ((PartitionLogRecord)r).Record.SubHeader.ParentPartitionId);
				});
		}

		[Fact]
		public void throws_on_unexpected_log_record_type() {
			var reader = new FakeReader(UnexpectedLogRecord);

			IPartitionManager partitionManager = new PartitionManager(reader, new FakeWriter(), new LogV3RecordFactory());

			Assert.Throws<ArgumentOutOfRangeException>(() => partitionManager.Initialize());
		}

		[Fact]
		public void throws_on_unexpected_system_log_record_type() {
			var reader = new FakeReader(UnexpectedSystemLogRecord);

			IPartitionManager partitionManager = new PartitionManager(reader, new FakeWriter(), new LogV3RecordFactory());

			Assert.Throws<ArgumentOutOfRangeException>(() => partitionManager.Initialize());
		}

		private LogV3StreamRecord UnexpectedLogRecord => new LogV3StreamRecord(
			streamId: Guid.NewGuid(),
			logPosition: 0,
			timeStamp: DateTime.UtcNow,
			streamNumber: 1,
			streamName: "unexpected-stream",
			partitionId: Guid.NewGuid());

		private SystemLogRecord UnexpectedSystemLogRecord => new SystemLogRecord(
			logPosition: 0,
			timeStamp: DateTime.UtcNow,
			systemRecordType: SystemRecordType.Invalid,
			systemRecordSerialization: SystemRecordSerialization.Invalid,
			data: new byte[0]);
	}

	class FakeWriter : ITransactionFileWriter {
		public long Position { get; }
		public long FlushedPosition { get; }

		public FakeWriter() {
			WrittenRecords = new List<ILogRecord>();
		}

		public bool IsFlushed { get; private set; }
		public List<ILogRecord> WrittenRecords { get; }

		public void Open() { }
		public bool CanWrite(int numBytes) => true;
		public bool Write(ILogRecord record, out long newPos) {
			WrittenRecords.Add(record);
			newPos = record.LogPosition + 1;
			return true;
		}

		public void OpenTransaction() => throw new NotImplementedException();

		public void WriteToTransaction(ILogRecord record, out long newPos) => throw new NotImplementedException();

		public bool TryWriteToTransaction(ILogRecord record, out long newPos) => throw new NotImplementedException();

		public void CommitTransaction() => throw new NotImplementedException();

		public bool HasOpenTransaction() => throw new NotImplementedException();

		public void Flush() {
			IsFlushed = true;
		}

		public void Close() { }

		public void Dispose() { }
	}

	class FakeReader : ITransactionFileReader {
		private readonly List<SeqReadResult> _results = new List<SeqReadResult>();
		private int _resultIndex = 0;
		private int _readCount = 0;

		public int ReadCount => _readCount;

		public FakeReader(ILogRecord record) {
			_results.Add(new SeqReadResult(true, false, record, 0, 0, 0));
		}

		public FakeReader(bool withoutRecords = false) : this(Guid.NewGuid(), Guid.NewGuid(), withoutRecords) {
		}

		public FakeReader(Guid? rootPartitionId, Guid? rootPartitionTypeId, bool withoutRecords = false) {
			if (withoutRecords) {
				_results.Add(SeqReadResult.Failure);
				return;
			}

			if (rootPartitionTypeId.HasValue) {
				var rootPartitionType = new PartitionTypeLogRecord(
					DateTime.UtcNow, 2, rootPartitionTypeId.Value, Guid.Empty, "Root");

				_results.Add(new SeqReadResult(true, false, rootPartitionType, 0, 0, 0));
			}

			if (rootPartitionId.HasValue && rootPartitionTypeId.HasValue) {
				var rootPartition = new PartitionLogRecord(
					DateTime.UtcNow, 3, rootPartitionId.Value, rootPartitionTypeId.Value, Guid.Empty, 0, 0, "Root");

				_results.Add(new SeqReadResult(true, false, rootPartition, 0, 0, 0));
			}
		}

		public void Reposition(long position) {
			_resultIndex = (int)position;
		}

		public SeqReadResult TryReadNext() {
			_readCount++;

			if (_resultIndex < _results.Count)
				return _results[_resultIndex++];

			return SeqReadResult.Failure;
		}

		public SeqReadResult TryReadPrev() {
			throw new NotImplementedException();
		}

		public RecordReadResult TryReadAt(long position, bool couldBeScavenged) {
			throw new NotImplementedException();
		}

		public bool ExistsAt(long position) {
			return true;
		}
	}
}
