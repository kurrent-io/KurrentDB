// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.Readers;
using KurrentDB.SecondaryIndexing.Storage;
using Serilog;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

class DefaultIndexProcessor : Disposable, ISecondaryIndexProcessor {
	private readonly DefaultIndexInFlightRecords _inFlightRecords;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly CategoryIndexProcessor _categoryIndexProcessor;
	private readonly EventTypeIndexProcessor _eventTypeIndexProcessor;
	private readonly StreamIndexProcessor _streamIndexProcessor;
	private readonly ISecondaryIndexProgressTracker _progressTracker;
	private readonly IPublisher _publisher;
	private Appender _appender;

	private static readonly ILogger Logger = Log.Logger.ForContext<DefaultIndexProcessor>();

	public long LastIndexedPosition { get; private set; }

	public DefaultIndexProcessor(
		DuckDbDataSource db,
		DefaultIndexInFlightRecords inFlightRecords,
		CategoryIndexProcessor categoryIndexProcessor,
		EventTypeIndexProcessor eventTypeIndexProcessor,
		StreamIndexProcessor streamIndexProcessor,
		ISecondaryIndexProgressTracker progressTracker,
		IPublisher publisher
	) {
		_connection = db.OpenNewConnection();
		_appender = new(_connection, "idx_all"u8);
		_inFlightRecords = inFlightRecords;

		_categoryIndexProcessor = categoryIndexProcessor;
		_eventTypeIndexProcessor = eventTypeIndexProcessor;
		_streamIndexProcessor = streamIndexProcessor;
		_progressTracker = progressTracker;
		_publisher = publisher;

		var lastPosition = GetLastPosition();
		Logger.Information("Last known log position: {Position}", lastPosition);
		LastIndexedPosition = lastPosition.PreparePosition;
	}

	public void Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed) return;

		var categoryId = _categoryIndexProcessor.Index(resolvedEvent);
		var eventTypeId = _eventTypeIndexProcessor.Index(resolvedEvent);
		var streamId = _streamIndexProcessor.Index(resolvedEvent);
		if (streamId == -1) {
			// StreamIndex is disposed
			return;
		}

		var logPosition = resolvedEvent.Event.LogPosition;
		var commitPosition = resolvedEvent.EventPosition?.CommitPosition;
		var eventNumber = resolvedEvent.Event.EventNumber;
		using (var row = _appender.CreateRow()) {
			row.Append(logPosition);
			if (commitPosition.HasValue && logPosition != commitPosition)
				row.Append(commitPosition.Value);
			else
				row.Append(DBNull.Value);
			row.Append(eventNumber);
			row.Append(new DateTimeOffset(resolvedEvent.Event.TimeStamp).ToUnixTimeMilliseconds());
			row.Append(DBNull.Value); // expires
			row.Append(streamId);
			row.Append(eventTypeId);
			row.Append(categoryId);
			row.Append(false); // is_deleted TODO: What happens if the event is deleted before we commit?
		}

		_inFlightRecords.Append(new(logPosition, categoryId, eventTypeId));
		LastIndexedPosition = resolvedEvent.Event.LogPosition;

		// _publisher.Publish(new StorageMessage.SecondaryIndexCommitted(resolvedEvent.ToResolvedLink(DefaultIndex.Name, sequence)));
		_progressTracker.RecordIndexed(resolvedEvent);
	}

	public void HandleStreamMetadataChange(ResolvedEvent evt) => _streamIndexProcessor.HandleStreamMetadataChange(evt);

	public TFPos GetLastPosition() {
		var result = _connection.QueryFirstOrDefault<LastPositionResult, GetLastLogPositionQuery>();
		return result != null ? new(result.Value.CommitPosition ?? result.Value.PreparePosition, result.Value.PreparePosition) : TFPos.Invalid;
	}

	public void Commit() {
		if (IsDisposingOrDisposed)
			return;

		_streamIndexProcessor.Commit();

		try {
			_progressTracker.RecordCommit(() => {
				_appender.Flush();
				return (LastIndexedPosition, _inFlightRecords.Count);
			});
		} catch (Exception e) {
			Logger.Error(e, "Failed to commit {Count} records to index at log position {LogPosition}", _inFlightRecords.Count, LastIndexedPosition);
			_progressTracker.RecordError(e);
			throw;
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
