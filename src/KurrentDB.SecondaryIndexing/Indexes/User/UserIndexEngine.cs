// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.SecondaryIndexing.Indexes.User.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

public sealed class UserIndexEngine :
		IHandle<SystemMessage.SystemReady>,
		IHandle<SystemMessage.BecomeShuttingDown>,
		IHandle<StorageMessage.EventCommitted>,
		IHostedService,
		ISecondaryIndexReader {
	private readonly ISystemClient _client;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly UserIndexEngineSubscription _subscription;
	private readonly ILogger<UserIndexEngine> _log;

	private CancellationTokenSource? _cts = new();

	private long _lastAppendedRecordPosition = -1;
	private DateTime _lastAppendedRecordTimestamp = DateTime.MinValue;

	public UserIndexEngine(
		ISystemClient client,
		IPublisher publisher,
		ISubscriber subscriber,
		ISchemaSerializer serializer,
		SecondaryIndexingPluginOptions options,
		DuckDBConnectionPool db,
		IReadIndex<string> index,
		TFChunkDbConfig chunkDbConfig,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)]
		Meter meter,
		ILoggerFactory loggerFactory) {
		_client = client;
		_log = loggerFactory.CreateLogger<UserIndexEngine>();
		_writerCheckpoint = chunkDbConfig.WriterCheckpoint;
		_subscription = new(client, publisher, serializer, options, db, index, meter, GetLastAppendedRecord, loggerFactory, _cts!.Token);

		subscriber.Subscribe<SystemMessage.SystemReady>(this);
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
		subscriber.Subscribe<StorageMessage.EventCommitted>(this);
	}

	public void EnsureLive() {
		if (!_subscription.CaughtUp) {
			throw new UserIndexesNotReadyException(_subscription.Checkpoint, _writerCheckpoint.Read());
		}
	}

	private (long, DateTime) GetLastAppendedRecord() {
		return (_lastAppendedRecordPosition, _lastAppendedRecordTimestamp);
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StopAsync(CancellationToken cancellationToken) {
		if (Interlocked.Exchange(ref _cts, null) is { } cts) {
			using (cts) {
				await cts.CancelAsync();
			}
		}

		try {
			await _subscription.Stop();
		} catch (Exception ex) {
			_log.LogUserIndexesFailedToStopSubscriptionToManagementStream(ex);
		}
	}

	public void Handle(SystemMessage.SystemReady message) {
		Task.Run(async () => {
			try {
				await ReadTail();
				_log.LogUserIndexesInitializingSubscriptionToManagementStream();
				await _subscription.Start();
			} catch (Exception ex) {
				_log.LogUserIndexesFailedToStartSubscriptionToManagementStream(ex);
			}
		});
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		_log.LogUserIndexesStoppingProcessingAsSystemIsShuttingDown();
		if (Interlocked.Exchange(ref _cts, null) is { } cts) {
			using (cts) {
				cts.Cancel();
			}
		}
	}

	private async Task ReadTail() {
		var lastRecord = await _client.Reading.ReadBackwards(Position.End, new Filter(), 1).FirstOrDefaultAsync();
		if (lastRecord != default && _lastAppendedRecordPosition == -1 && _lastAppendedRecordTimestamp == DateTime.MinValue) {
			_lastAppendedRecordPosition = lastRecord.OriginalPosition!.Value.CommitPosition;
			_lastAppendedRecordTimestamp = lastRecord.Event.TimeStamp;
		}
	}

	public void Handle(StorageMessage.EventCommitted message) {
		if (!message.Event.EventStreamId.StartsWith('$')) {
			_lastAppendedRecordPosition = message.CommitPosition;
			_lastAppendedRecordTimestamp = message.Event.TimeStamp;
		}
	}

	private class Filter : IEventFilter {
		public bool IsEventAllowed(EventRecord eventRecord) => !eventRecord.EventStreamId.StartsWith('$');
	}

	public bool CanReadIndex(string indexStream) => _subscription.CanReadIndex(indexStream);

	public TFPos GetLastIndexedPosition(string indexStream) => _subscription.GetLastIndexedPosition(indexStream);

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg,
		CancellationToken token) =>
		_subscription.ReadForwards(msg, token);

	public ValueTask<ClientMessage.ReadIndexEventsBackwardCompleted> ReadBackwards(ClientMessage.ReadIndexEventsBackward msg,
		CancellationToken token) =>
		_subscription.ReadBackwards(msg, token);

	public bool TryGetUserIndexTableDetails(string indexName, out string tableName, out string inFlightTableName, out string? fieldName) =>
		_subscription.TryGetUserIndexTableDetails(indexName, out tableName, out inFlightTableName, out fieldName);
}

static partial class UserIndexEngineLogMessages {
	[LoggerMessage(LogLevel.Error, "User indexes: Failed to stop subscription to management stream")]
	internal static partial void LogUserIndexesFailedToStopSubscriptionToManagementStream(this ILogger logger, Exception exception);

	[LoggerMessage(LogLevel.Trace, "User indexes: Initializing subscription to management stream")]
	internal static partial void LogUserIndexesInitializingSubscriptionToManagementStream(this ILogger logger);

	[LoggerMessage(LogLevel.Error, "User indexes: Failed to start subscription to management stream")]
	internal static partial void LogUserIndexesFailedToStartSubscriptionToManagementStream(this ILogger logger, Exception exception);

	[LoggerMessage(LogLevel.Trace, "User indexes: Stopping processing as system is shutting down")]
	internal static partial void LogUserIndexesStoppingProcessingAsSystemIsShuttingDown(this ILogger logger);
}
