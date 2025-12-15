// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
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
using KurrentDB.SecondaryIndexing.Indexes.Custom.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom;

public sealed class CustomIndexEngine :
	IHandle<SystemMessage.SystemReady>,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<StorageMessage.EventCommitted>,
	IHostedService,
	ISecondaryIndexReader {

	private readonly ISystemClient _client;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly CustomIndexEngineSubscription _subscription;

	private CancellationTokenSource? _cts;

	private long _lastAppendedRecordPosition = -1;
	private DateTime _lastAppendedRecordTimestamp = DateTime.MinValue;

	private static readonly ILogger Log = Serilog.Log.ForContext<CustomIndexEngine>();

	[Experimental("SECONDARY_INDEX")]
	public CustomIndexEngine(
		ISystemClient client,
		IPublisher publisher,
		ISubscriber subscriber,
		ISchemaSerializer serializer,
		SecondaryIndexingPluginOptions options,
		DuckDBConnectionPool db,
		IReadIndex<string> index,
		TFChunkDbConfig chunkDbConfig,
		[FromKeyedServices(SecondaryIndexingConstants.InjectionKey)] Meter meter) {

		_client = client;
		_writerCheckpoint = chunkDbConfig.WriterCheckpoint;
		_cts = new CancellationTokenSource();
		_subscription = new CustomIndexEngineSubscription(client, publisher, serializer, options, db, index, meter, GetLastAppendedRecord, _cts!.Token);

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
			Log.Error(ex, "Custom indexes: Failed to stop subscription to management stream");
		}
	}

	public void Handle(SystemMessage.SystemReady message) {
		Task.Run(async () => {
			try {
				await ReadTail();
				Log.Verbose("Custom indexes: Initializing subscription to management stream");
				await _subscription.Start();
			} catch (Exception ex) {
				Log.Error(ex, "Custom indexes: Failed to start subscription to management stream");
			}
		});
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		Log.Verbose("Custom indexes: Stopping processing as system is shutting down");
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

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) =>
		_subscription.ReadForwards(msg, token);

	public ValueTask<ClientMessage.ReadIndexEventsBackwardCompleted> ReadBackwards(ClientMessage.ReadIndexEventsBackward msg, CancellationToken token) =>
		_subscription.ReadBackwards(msg, token);

	public bool TryGetCustomIndexTableDetails(string indexName, out string tableName, out string inFlightTableName, out bool hasFields) =>
		_subscription.TryGetCustomIndexTableDetails(indexName, out tableName, out inFlightTableName, out hasFields);
}
