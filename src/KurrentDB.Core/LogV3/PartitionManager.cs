// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.LogV3;

public class PartitionManager : IPartitionManager, IReadCursor {
	private static readonly ILogger _log = Log.ForContext<PartitionManager>();
	private readonly ITransactionFileReader _reader;
	private readonly ITransactionFileWriter _writer;
	private readonly LogV3RecordFactory _recordFactory;
	private long _position;

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

	long IReadCursor.Position {
		get => _position;
		set => _position = value;
	}

	public async ValueTask Initialize(CancellationToken token) {
		if (RootId.HasValue)
			return;

		await ReadRootPartition(token);
		await EnsureRootPartitionIsWritten(token);
	}

	private async ValueTask EnsureRootPartitionIsWritten(CancellationToken token) {
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

			if (await _writer.Write(rootPartitionType, token) is (false, _))
				throw new Exception($"Failed to write root partition type!");

			await _writer.Flush(token);

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

			if (await _writer.Write(rootPartition, token) is (false, _))
				throw new Exception($"Failed to write root partition!");

			await _writer.Flush(token);

			_recordFactory.SetRootPartitionId(RootId.Value);

			_log.Debug("Root partition created, id: {id}", RootId);
		}
	}

	private async ValueTask ReadRootPartition(CancellationToken token) {
		SeqReadResult result;
		_position = 0L;
		while ((result = await _reader.TryReadNext(this, token)).Success) {
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
