// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using DotNext.Threading;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.SecondaryIndexing.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;
using static KurrentDB.Protobuf.Server.Properties;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexProcessor : Disposable, ISecondaryIndexProcessor {
	private readonly DefaultIndexInFlightRecords _inFlightRecords;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly ISecondaryIndexProgressTracker _progressTracker;
	private readonly IPublisher _publisher;
	private readonly ILongHasher<string> _hasher;
	private Appender _appender;

	private static readonly ILogger Logger = Log.Logger.ForContext<DefaultIndexProcessor>();

	public long LastIndexedPosition { get; private set; }

	public DefaultIndexProcessor(
		DuckDBConnectionPool db,
		IndexingDbSchema schema,
		DefaultIndexInFlightRecords inFlightRecords,
		ISecondaryIndexProgressTracker progressTracker,
		IPublisher publisher,
		ILongHasher<string> hasher
	) {
		schema.CreateSchema();
		_connection = db.Open();
		_appender = new(_connection, "idx_all"u8);
		_inFlightRecords = inFlightRecords;
		_progressTracker = progressTracker;
		_publisher = publisher;
		_hasher = hasher;

		var (lastPosition, rowId) = GetLastPosition();
		Logger.Information("Last known log position: {Position}", lastPosition);
		LastIndexedPosition = lastPosition.PreparePosition;
		_rowId = rowId;
	}

	public void Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed) return;

		string? schemaFormat = null;
		string? schemaId = null;
		if (resolvedEvent.Event.Properties.Length > 0) {
			var props = Parser.ParseFrom(resolvedEvent.Event.Properties.Span);
			schemaId = props.PropertiesValues.TryGetValue(Constants.Properties.SchemaId, out var schemaIdValue) ? schemaIdValue.StringValue : null;
			schemaFormat = props.PropertiesValues.TryGetValue(Constants.Properties.DataFormatKey, out var dataFormatValue) ? dataFormatValue.StringValue : null;
		}

		var schemaName = resolvedEvent.Event.EventType;
		schemaFormat ??= (resolvedEvent.Event.IsJson ? Constants.Properties.DataFormats.Json : Constants.Properties.DataFormats.Bytes);

		var logPosition = resolvedEvent.Event.LogPosition;
		var commitPosition = resolvedEvent.EventPosition?.CommitPosition;
		var eventNumber = resolvedEvent.Event.EventNumber;
		var streamHash = _hasher.Hash(resolvedEvent.Event.EventStreamId);
		var category = GetStreamCategory(resolvedEvent.Event.EventStreamId);
		var created = new DateTimeOffset(resolvedEvent.Event.TimeStamp).ToUnixTimeMilliseconds();
		using (var row = _appender.CreateRow()) {
			row.Append(logPosition);
			if (commitPosition.HasValue && logPosition != commitPosition)
				row.Append(commitPosition.Value);
			else
				row.Append(DBNull.Value);
			row.Append(eventNumber);
			row.Append(created);
			row.Append(DBNull.Value); // expires
			row.Append(resolvedEvent.Event.EventStreamId);
			row.Append(streamHash);
			row.Append(schemaName);
			row.Append(category);
			row.Append(false); // is_deleted TODO: What happens if the event is deleted before we commit?
			if (schemaId != null) {
				row.Append(schemaId);
			} else {
				row.Append(DBNull.Value);
			}
			row.Append(schemaFormat);
		}

		_inFlightRecords.Append(logPosition, category, schemaName, resolvedEvent.Event.EventStreamId, eventNumber, created);
		LastIndexedPosition = resolvedEvent.Event.LogPosition;

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(SystemStreams.DefaultSecondaryIndex, resolvedEvent));
		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(EventTypeIndex.Name(schemaName), resolvedEvent));
		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(CategoryIndex.Name(category), resolvedEvent));
		_progressTracker.RecordIndexed(resolvedEvent);
		return;

		static string GetStreamCategory(string streamName) {
			var dashIndex = streamName.IndexOf('-');
			return dashIndex == -1 ? streamName : streamName[..dashIndex];
		}
	}

	public (TFPos, long) GetLastPosition() {
		var result = _connection.QueryFirstOrDefault<LastPositionResult, GetLastLogPositionQuery>();
		return result != null ?
			(new(result.Value.CommitPosition ?? result.Value.PreparePosition, result.Value.PreparePosition), result.Value.RowId)
			: (TFPos.Invalid, 0);
	}

	private Atomic.Boolean _committing;
	private long _rowId;

	public void Commit() {
		if (IsDisposingOrDisposed || !_committing.FalseToTrue())
			return;

		try {
			_progressTracker.RecordCommit(() => {
				_appender.Flush();
				return (LastIndexedPosition, _inFlightRecords.Count);
			});
		} catch (Exception e) {
			Logger.Error(e, "Failed to commit {Count} records to index at log position {LogPosition}", _inFlightRecords.Count, LastIndexedPosition);
			_progressTracker.RecordError(e);
			throw;
		} finally {
			_committing.TrueToFalse();
		}

		_inFlightRecords.Clear();
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Commit();
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
