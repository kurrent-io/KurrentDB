// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
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

	public TFPos LastIndexedPosition { get; private set; } = TFPos.HeadOfTf;

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

		SetLastPosition();
		Logger.Information("Last known log position: {Position}", LastIndexedPosition);
	}

	public void Index(ResolvedEvent resolvedEvent) {
		ObjectDisposedException.ThrowIf(IsDisposingOrDisposed, nameof(DefaultIndexProcessor));

		var categoryId = _categoryIndexProcessor.Index(resolvedEvent);
		var eventTypeId = _eventTypeIndexProcessor.Index(resolvedEvent);
		var streamId = _streamIndexProcessor.Index(resolvedEvent);

		long preparePosition = resolvedEvent.OriginalPosition!.Value.PreparePosition;
		long commitPosition = resolvedEvent.OriginalPosition!.Value.CommitPosition;

		var eventNumber = resolvedEvent.Event.EventNumber;
		using (var row = _appender.CreateRow()) {
			row.Append(preparePosition);
			if (preparePosition != commitPosition)
				row.Append(commitPosition);
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

		_inFlightRecords.Append(new TFPos(commitPosition, preparePosition), categoryId, eventTypeId);
		LastIndexedPosition = resolvedEvent.OriginalPosition!.Value;

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(SystemStreams.DefaultSecondaryIndex, resolvedEvent));
		_progressTracker.RecordIndexed(resolvedEvent);
	}

	public void HandleStreamMetadataChange(ResolvedEvent evt) => _streamIndexProcessor.HandleStreamMetadataChange(evt);

	private void SetLastPosition() {
		var result = _connection.QueryFirstOrDefault<LastPositionResult, GetLastLogPositionQuery>();
		if (result is null)
			return;

		LastIndexedPosition = new(result.Value.CommitPosition ?? result.Value.PreparePosition, result.Value.PreparePosition);
	}

	public void Commit() {
		ObjectDisposedException.ThrowIf(IsDisposingOrDisposed, nameof(DefaultIndexProcessor));

		try {
			_progressTracker.RecordCommit(() => {
				_streamIndexProcessor.Commit();
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
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
