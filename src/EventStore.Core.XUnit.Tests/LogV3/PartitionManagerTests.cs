// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using EventStore.Core.LogAbstraction;
using EventStore.Core.LogV3;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using FluentAssertions;
using Xunit;

namespace EventStore.Core.XUnit.Tests.LogV3;


public class PartitionManagerTests {

	[Fact]
	public async Task creates_new_root_partition_on_first_epoch() {
		var reader = new FakeReader(withoutRecords: true);
		var writer = new FakeWriter();

		IPartitionManager partitionManager = new PartitionManager(reader, writer, new LogV3RecordFactory());

		await partitionManager.Initialize(CancellationToken.None);

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
	public async Task configures_record_factory_with_root_partition_id() {
		var reader = new FakeReader();
		var recordFactory = new LogV3RecordFactory();

		IPartitionManager partitionManager = new PartitionManager(reader, new FakeWriter(), recordFactory);

		await partitionManager.Initialize(CancellationToken.None);

		var streamRecord = (LogV3StreamRecord)recordFactory.CreateStreamRecord(Guid.NewGuid(), 1, DateTime.UtcNow, 1, "stream-1");
		Assert.Equal(partitionManager.RootId, streamRecord.Record.SubHeader.PartitionId);
	}

	[Fact]
	public async Task reads_root_partition_once_initialized() {
		var rootPartitionId = Guid.NewGuid();
		var rootPartitionTypeId = Guid.NewGuid();
		var reader = new FakeReader(rootPartitionId, rootPartitionTypeId);
		var writer = new FakeWriter();

		IPartitionManager partitionManager = new PartitionManager(reader, writer, new LogV3RecordFactory());

		await partitionManager.Initialize(CancellationToken.None);

		Assert.Empty(writer.WrittenRecords);
		Assert.Equal(rootPartitionId, partitionManager.RootId);
		Assert.Equal(rootPartitionTypeId, partitionManager.RootTypeId);
	}

	[Fact]
	public async Task reads_root_partition_only_once() {
		var reader = new FakeReader();
		var writer = new FakeWriter();

		IPartitionManager partitionManager = new PartitionManager(reader, writer, new LogV3RecordFactory());

		await partitionManager.Initialize(CancellationToken.None);
		await partitionManager.Initialize(CancellationToken.None);

		Assert.Empty(writer.WrittenRecords);
		Assert.Equal(2, reader.ReadCount);
	}

	[Fact]
	public async Task creates_root_partition_in_case_it_partially_failed_previously() {
		var rootPartitionTypeId = Guid.NewGuid();
		var reader = new FakeReader(rootPartitionId: null, rootPartitionTypeId);
		var writer = new FakeWriter();

		IPartitionManager partitionManager = new PartitionManager(reader, writer, new LogV3RecordFactory());

		await partitionManager.Initialize(CancellationToken.None);

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
	public async Task throws_on_unexpected_log_record_type() {
		var reader = new FakeReader(UnexpectedLogRecord);

		IPartitionManager partitionManager = new PartitionManager(reader, new FakeWriter(), new LogV3RecordFactory());

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => partitionManager.Initialize(CancellationToken.None).AsTask());
	}

	[Fact]
	public async Task throws_on_unexpected_system_log_record_type() {
		var reader = new FakeReader(UnexpectedSystemLogRecord);

		IPartitionManager partitionManager = new PartitionManager(reader, new FakeWriter(), new LogV3RecordFactory());

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => partitionManager.Initialize(CancellationToken.None).AsTask());
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

class FakeWriter: ITransactionFileWriter {
	public long Position { get; }
	public long FlushedPosition { get; }

	public FakeWriter() {
		WrittenRecords = new List<ILogRecord>();
	}

	public bool IsFlushed { get; private set; }
	public List<ILogRecord> WrittenRecords { get; }

	public ValueTask Open(CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled(token) : ValueTask.CompletedTask;

	public bool CanWrite(int numBytes) => true;

	public ValueTask<(bool, long)> Write(ILogRecord record, CancellationToken token) {
		WrittenRecords.Add(record);
		return new((true, record.LogPosition + 1));
	}

	public void OpenTransaction() => throw new NotImplementedException();

	public ValueTask<long?> WriteToTransaction(ILogRecord record, CancellationToken token)
		=> ValueTask.FromException<long?>(new NotImplementedException());

	public void CommitTransaction() => throw new NotImplementedException();

	public bool HasOpenTransaction() => throw new NotImplementedException();

	public ValueTask Flush(CancellationToken token) {
		IsFlushed = true;
		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

class FakeReader : ITransactionFileReader {
	private readonly List<SeqReadResult> _results = new();
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
		_resultIndex = (int) position;
	}

	public ValueTask<SeqReadResult> TryReadNext(CancellationToken token) {
		_readCount++;

		return new(_resultIndex < _results.Count
			? _results[_resultIndex++]
			: SeqReadResult.Failure);
	}

	public ValueTask<SeqReadResult> TryReadPrev(CancellationToken token)
		=> ValueTask.FromException<SeqReadResult>(new NotImplementedException());

	public ValueTask<RecordReadResult> TryReadAt(long position, bool couldBeScavenged, CancellationToken token)
		=> ValueTask.FromException<RecordReadResult>(new NotImplementedException());

	public ValueTask<bool> ExistsAt(long position, CancellationToken token)
		=> new(true);
}
