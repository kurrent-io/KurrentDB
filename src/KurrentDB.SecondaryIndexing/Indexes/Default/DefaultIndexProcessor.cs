// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using DotNext;
using DotNext.Threading;
using DuckDB.NET.Data;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Quack.Threading;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.SecondaryIndexing.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexProcessor : Disposable, ISecondaryIndexProcessor {
	private readonly DuckDBAdvancedConnection _connection;
	private readonly IPublisher _publisher;
	private readonly ILongHasher<string> _hasher;
	private readonly ILogger<DefaultIndexProcessor> _log;
	private readonly BufferedView _appender;

	public TFPos LastIndexedPosition { get; private set; }

	public DefaultIndexProcessor(
		DuckDBConnectionPool db,
		IPublisher publisher,
		ILongHasher<string> hasher,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)]
		Meter meter,
		ILoggerFactory loggerFactory,
		MetricsConfiguration? metricsConfiguration = null,
		TimeProvider? clock = null
	) {
		_connection = db.Open();
		_log = loggerFactory.CreateLogger<DefaultIndexProcessor>();
		_appender = new(_connection, "idx_all", "log_position", DefaultIndexViewName);
		var serviceName = metricsConfiguration?.ServiceName ?? "kurrentdb";
		Tracker = new("default", serviceName, meter, clock ?? TimeProvider.System,
			loggerFactory.CreateLogger<SecondaryIndexProgressTracker>());
		_publisher = publisher;
		_hasher = hasher;

		var (lastPosition, lastTimestamp) = ReadLastIndexedRecord();
		_log.LogInformation("Last known log position: {Position}", lastPosition);
		LastIndexedPosition = lastPosition;
		Tracker.InitLastIndexed(lastPosition.CommitPosition, lastTimestamp);
	}

	public bool TryIndex(ResolvedEvent resolvedEvent) {
		if (IsDisposingOrDisposed)
			return false;

		string? schemaFormat = null;
		string? schemaId = null;
		if (resolvedEvent.Event.Properties.Length > 0) {
			var props = Struct.Parser.ParseFrom(resolvedEvent.Event.Properties.Span);
			schemaId = props.Fields.TryGetValue(Constants.RecordProperties.SchemaIdKey, out var schemaIdValue)
				? schemaIdValue.StringValue
				: null;
			schemaFormat = props.Fields.TryGetValue(Constants.RecordProperties.SchemaFormatKey, out var dataFormatValue)
				? dataFormatValue.StringValue
				: null;
		}

		var schemaName = resolvedEvent.Event.EventType;
		schemaFormat ??= resolvedEvent.Event.IsJson ? "Json" : "Bytes";

		var stream = resolvedEvent.Event.EventStreamId;
		var logPosition = resolvedEvent.Event.LogPosition;
		var commitPosition = resolvedEvent.EventPosition?.CommitPosition;
		var eventNumber = resolvedEvent.Event.EventNumber;
		var streamHash = _hasher.Hash(stream);
		var category = GetStreamCategory(resolvedEvent.Event.EventStreamId);
		var created = new DateTimeOffset(resolvedEvent.Event.TimeStamp).ToUnixTimeMilliseconds();
		using (var row = _appender.CreateRow()) {
			row.Add(logPosition);
			if (commitPosition.HasValue && logPosition != commitPosition.GetValueOrDefault())
				row.Add(commitPosition.Value);
			else
				row.Add(DBNull.Value);
			row.Add(eventNumber);
			row.Add(created);
			row.Add(DBNull.Value); // expires
			row.Add(stream);
			row.Add(streamHash);
			row.Add(schemaName);
			row.Add(category);
			row.Add(false); // is_deleted TODO: What happens if the event is deleted before we commit?
			if (schemaId != null) {
				row.Add(schemaId);
			} else {
				row.Add(DBNull.Value);
			}

			row.Add(schemaFormat);
		}

		LastIndexedPosition = resolvedEvent.EventPosition!.Value;

		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(SystemStreams.DefaultSecondaryIndex, resolvedEvent));
		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(EventTypeIndex.Name(schemaName), resolvedEvent));
		_publisher.Publish(new StorageMessage.SecondaryIndexCommitted(CategoryIndex.Name(category), resolvedEvent));
		Tracker.RecordIndexed(resolvedEvent);

		return true;

		static string GetStreamCategory(string streamName) {
			var dashIndex = streamName.IndexOf('-');
			return dashIndex == -1 ? streamName : streamName[..dashIndex];
		}
	}

	public TFPos GetLastPosition() => LastIndexedPosition;

	private (TFPos, DateTimeOffset) ReadLastIndexedRecord() {
		return _connection.QueryFirstOrDefault<LastPositionResult, GetLastLogPositionQuery>().TryGet(out var result)
			? (new TFPos(result.CommitPosition ?? result.PreparePosition, result.PreparePosition),
				DateTimeOffset.FromUnixTimeMilliseconds(result.Timestamp))
			: (TFPos.Invalid, DateTimeOffset.MinValue);
	}

	public SecondaryIndexProgressTracker Tracker { get; }

	private Atomic.Boolean _committing;

	/// <summary>
	/// Commits all in-flight records to the index.
	/// </summary>
	public void Commit() {
		if (IsDisposingOrDisposed || !_committing.FalseToTrue())
			return;

		try {
			using var duration = Tracker.StartCommitDuration();
			_appender.Flush();
		} catch (Exception e) {
			_log.LogError(e, "Failed to commit records to index at log position {LogPosition}", LastIndexedPosition);
			throw;
		} finally {
			_committing.TrueToFalse();
		}
	}

	public BufferedView.Snapshot CaptureSnapshot(DuckDBConnection connection) => _appender.TakeSnapshot(connection);

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Commit();
			_appender.Unregister(_connection);
			_appender.Dispose();
			_connection.Dispose();
		}

		base.Dispose(disposing);
	}
}
