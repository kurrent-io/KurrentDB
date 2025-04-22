// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_writing_prepare_record_with_schema_to_file<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private ITransactionFileWriter _writer;
	private InMemoryCheckpoint _writerCheckpoint;
	private readonly Guid _eventId = Guid.NewGuid();
	private readonly Guid _correlationId = Guid.NewGuid();
	private readonly Guid _dataSchemaVersionId = Guid.NewGuid();
	private readonly Guid _metadataSchemaVersionId = Guid.NewGuid();

	private readonly KurrentDB.Core.Data.SchemaInfo.SchemaDataFormat _dataFormat =
		KurrentDB.Core.Data.SchemaInfo.SchemaDataFormat.Json;

	private readonly KurrentDB.Core.Data.SchemaInfo.SchemaDataFormat _metadataFormat =
		KurrentDB.Core.Data.SchemaInfo.SchemaDataFormat.Avro;

	private IPrepareLogRecord<TStreamId> _record;
	private TFChunkDb _db;

	[OneTimeSetUp]
	public async Task SetUp() {
		_writerCheckpoint = new InMemoryCheckpoint();
		_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, _writerCheckpoint, new InMemoryCheckpoint(),
			1024));
		await _db.Open();
		_writer = new TFChunkWriter(_db);
		await _writer.Open(CancellationToken.None);

		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		_record = LogRecord.Prepare(
			factory: recordFactory,
			logPosition: 0,
			eventId: _eventId,
			correlationId: _correlationId,
			transactionPos: 0xDEAD,
			transactionOffset: 0xBEEF,
			eventStreamId: streamId,
			expectedVersion: 1234,
			timeStamp: new DateTime(2012, 12, 21),
			flags: PrepareFlags.SingleWrite,
			eventType: eventTypeId,
			data: new byte[] { 1, 2, 3, 4, 5 },
			metadata: new byte[] { 7, 17 },
			dataSchemaInfo: new KurrentDB.Core.Data.SchemaInfo(_dataFormat, _dataSchemaVersionId),
			metadataSchemaInfo: new KurrentDB.Core.Data.SchemaInfo(_metadataFormat, _metadataSchemaVersionId));

		await _writer.Write(_record, CancellationToken.None);
		await _writer.Flush(CancellationToken.None);
	}

	[OneTimeTearDown]
	public async Task Teardown() {
		await _writer.DisposeAsync();
		await _db.DisposeAsync();
	}

	[Test]
	public async Task the_data_is_written() {
		//TODO MAKE THIS ACTUALLY ASSERT OFF THE FILE AND READER FROM KNOWN FILE
		using var reader = new TFChunkChaser(_db, _writerCheckpoint, _db.Config.ChaserCheckpoint);
		reader.Open();
		ILogRecord r = await reader.TryReadNext(CancellationToken.None) is { Success: true } res
			? res.LogRecord
			: null;

		Assert.NotNull(r);

		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		Assert.True(r is IPrepareLogRecord<TStreamId>);
		var p = (IPrepareLogRecord<TStreamId>)r;
		Assert.AreEqual(p.RecordType, LogRecordType.Prepare);
		Assert.AreEqual(p.LogPosition, 0);
		Assert.AreEqual(p.TransactionPosition, 0xDEAD);
		Assert.AreEqual(p.TransactionOffset, 0xBEEF);
		Assert.AreEqual(p.CorrelationId, _correlationId);
		Assert.AreEqual(p.EventId, _eventId);
		Assert.AreEqual(p.EventStreamId, streamId);
		Assert.AreEqual(p.ExpectedVersion, 1234);
		Assert.That(p.TimeStamp, Is.EqualTo(new DateTime(2012, 12, 21)).Within(7).Milliseconds);
		Assert.AreEqual(p.Flags, PrepareFlags.SingleWrite | PrepareFlags.HasDataSchema | PrepareFlags.HasMetadataSchema);
		Assert.AreEqual(p.EventType, eventTypeId);
		Assert.AreEqual(p.Data.Length, 5);
		Assert.AreEqual(p.Metadata.Length, 2);
		Assert.AreEqual(_dataSchemaVersionId, p.DataSchemaInfo.SchemaVersionId);
		Assert.AreEqual(_dataFormat, p.DataSchemaInfo.SchemaFormat);
		Assert.AreEqual(_metadataSchemaVersionId, p.MetadataSchemaInfo.SchemaVersionId);
		Assert.AreEqual(_metadataFormat, p.MetadataSchemaInfo.SchemaFormat);
	}

	[Test]
	public void the_checksum_is_updated() {
		Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), _writerCheckpoint.Read());
	}

	[Test]
	public async Task trying_to_read_past_writer_checksum_returns_false() {
		var reader = new TFChunkReader(_db, _writerCheckpoint);
		Assert.IsFalse((await reader.TryReadAt(_writerCheckpoint.Read(), couldBeScavenged: true, CancellationToken.None)).Success);
	}
}
