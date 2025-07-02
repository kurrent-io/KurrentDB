// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
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

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexProcessor : Disposable, ISecondaryIndexProcessor {
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
	public long LastSequence;

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
		_appender = new Appender(_connection, "idx_all"u8);
		_inFlightRecords = inFlightRecords;

		_categoryIndexProcessor = categoryIndexProcessor;
		_eventTypeIndexProcessor = eventTypeIndexProcessor;
		_streamIndexProcessor = streamIndexProcessor;
		_progressTracker = progressTracker;
		_publisher = publisher;

		var lastSequence = GetLastSequence();
		Logger.Information("Last known global sequence: {Seq}", lastSequence);
		LastSequence = lastSequence.HasValue ? lastSequence.Value + 1 : 0;
	}

	public void Index(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed) return;

		var category = _categoryIndexProcessor.Index(resolvedEvent);
		var eventType = _eventTypeIndexProcessor.Index(resolvedEvent);
		var streamId = _streamIndexProcessor.Index(resolvedEvent);
		if (streamId == -1) {
			// StreamIndex is disposed
			return;
		}

		var sequence = LastSequence++;
		var logPosition = resolvedEvent.Event.LogPosition;
		var commitPosition = resolvedEvent.EventPosition?.CommitPosition;
		var eventNumber = resolvedEvent.Event.EventNumber;
		using (var row = _appender.CreateRow()) {
			row.Append(sequence);
			row.Append(eventNumber);
			row.Append(logPosition);
			if(commitPosition.HasValue && logPosition != commitPosition)
				row.Append(commitPosition.Value);
			else
				row.AppendDefault();
			row.Append(new DateTimeOffset(resolvedEvent.Event.TimeStamp).ToUnixTimeMilliseconds());
			row.AppendDefault(); // expires
			row.Append(streamId);
			row.Append(eventType.Id);
			row.Append(eventType.Sequence);
			row.Append(category.Id);
			row.Append(category.Sequence);
			row.Append(false); // is_deleted TODO: What happens if the event is deleted before we commit?
		}

		_inFlightRecords.Append(
			new(
				sequence,
				logPosition,
				category.Id,
				category.Sequence,
				eventType.Id,
				eventType.Sequence
			)
		);
		LastIndexedPosition = resolvedEvent.Event.LogPosition;

		_publisher.Publish(
			new StorageMessage.SecondaryIndexCommitted(resolvedEvent.ToResolvedLink(DefaultIndex.Name, sequence))
		);
		_progressTracker.RecordIndexed(resolvedEvent);
	}

	public void HandleStreamMetadataChange(ResolvedEvent evt) {
		_streamIndexProcessor.HandleStreamMetadataChange(evt);
	}

	public long? GetLastPosition() =>
		_connection.QueryFirstOrDefault<Optional<long>, DefaultSql.GetLastLogPositionSql>()?.OrNull();


	private long? GetLastSequence() =>
		_connection.QueryFirstOrDefault<Optional<long>, DefaultSql.GetLastSequenceSql>()?.OrNull();


	private readonly Stopwatch _sw = new();

	public void Commit() {
		if (IsDisposingOrDisposed)
			return;

		_streamIndexProcessor.Commit();

		try {

			_progressTracker.RecordCommit(() => {
				_appender.Flush();
				return (LastSequence, _inFlightRecords.Count);
			});
			_sw.Restart();
			_appender.Flush();
			_sw.Stop();
			Logger.Debug("Committed {Count} records to index at seq {Seq} ({Took} ms)", _inFlightRecords.Count,
				LastSequence, _sw.ElapsedMilliseconds);

		} catch (Exception e) {
			Logger.Error(e, "Failed to commit {Count} records to index at sequence {Seq}", _inFlightRecords.Count,
				LastSequence);
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
