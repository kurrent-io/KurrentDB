// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using DotNext;
using DotNext.Threading;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
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
using Microsoft.Extensions.Logging.Abstractions;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

internal class DefaultIndexProcessor : Disposable, ISecondaryIndexProcessor {
	private readonly DefaultIndexInFlightRecords _inFlightRecords;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly IPublisher _publisher;
	private readonly ILongHasher<string> _hasher;
	private readonly ILogger<DefaultIndexProcessor> _log;
	private Appender _appender;

	public TFPos LastIndexedPosition { get; private set; }

	public DefaultIndexProcessor(
		DuckDBConnectionPool db,
		DefaultIndexInFlightRecords inFlightRecords,
		IPublisher publisher,
		ILongHasher<string> hasher,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)]
		Meter meter,
		MetricsConfiguration? metricsConfiguration = null,
		TimeProvider? clock = null,
		ILogger<DefaultIndexProcessor>? log = null
	) {
		_connection = db.Open();
		_appender = new(_connection, "idx_all"u8);
		_inFlightRecords = inFlightRecords;
		_log = log ?? NullLogger<DefaultIndexProcessor>.Instance;
		var serviceName = metricsConfiguration?.ServiceName ?? "kurrentdb";
		Tracker = new("default", serviceName, meter, clock ?? TimeProvider.System);
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
			row.Append(logPosition);
			if (commitPosition.HasValue && logPosition != commitPosition)
				row.Append(commitPosition.Value);
			else
				row.Append(DBNull.Value);
			row.Append(eventNumber);
			row.Append(created);
			row.Append(DBNull.Value); // expires
			row.Append(stream);
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

		_inFlightRecords.Append(logPosition, commitPosition ?? logPosition, category, schemaName, resolvedEvent.Event.EventStreamId,
			eventNumber, created);
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
		var result = _connection.QueryFirstOrDefault<LastPositionResult, GetLastLogPositionQuery>();
		return result != null
			? (new(result.Value.CommitPosition ?? result.Value.PreparePosition, result.Value.PreparePosition),
				DateTimeOffset.FromUnixTimeMilliseconds(result.Value.Timestamp))
			: (TFPos.Invalid, DateTimeOffset.MinValue);
	}

	public SecondaryIndexProgressTracker Tracker { get; }

	private Atomic.Boolean _committing;

	public void Commit() => Commit(true);

	/// <summary>
	/// Commits all in-flight records to the index.
	/// </summary>
	/// <param name="clearInflight">Tells you whether to clear the in-flight records after committing. It must be true and only set to false in tests.</param>
	internal void Commit(bool clearInflight) {
		if (IsDisposingOrDisposed || !_committing.FalseToTrue())
			return;

		try {
			using var duration = Tracker.StartCommitDuration();
			_appender.Flush();
		} catch (Exception e) {
			_log.LogError(e, "Failed to commit {Count} records to index at log position {LogPosition}",
				_inFlightRecords.Count, LastIndexedPosition);
			throw;
		} finally {
			_committing.TrueToFalse();
		}

		if (clearInflight) {
			_inFlightRecords.Clear();
		}
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
