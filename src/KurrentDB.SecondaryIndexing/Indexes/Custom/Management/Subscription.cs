// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.Channels;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.SecondaryIndexing.Subscriptions;
using Polly;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Custom.Management;

public class Subscription {
	private const string ManagementStream = "$secondary-indexes-custom";

	private readonly IPublisher _publisher;
	private readonly SecondaryIndexingPluginOptions _options;
	private readonly DuckDBConnectionPool _db;
	private readonly Meter _meter;
	private readonly Channel<ReadResponse> _channel;
	private readonly Dictionary<string, CustomIndexSubscription> _subscriptions = new();
	private readonly CancellationTokenSource _cts;

	private static readonly ILogger Log = Serilog.Log.ForContext<Subscription>();

	public Subscription(IPublisher publisher,
		SecondaryIndexingPluginOptions options,
		DuckDBConnectionPool db,
		Meter meter,
		CancellationToken token) {
		_publisher = publisher;
		_options = options;
		_db = db;
		_meter = meter;

		_cts = CancellationTokenSource.CreateLinkedTokenSource(token);
		_channel = Channel.CreateUnbounded<ReadResponse>();
	}

	public async Task Start() {
		try {
			await StartInternal();
		} catch (OperationCanceledException) {
			Log.Verbose("Subscription to: {stream} is shutting down.", ManagementStream);
		} catch (Exception ex) {
			Log.Fatal(ex, "Subscription to: {stream} has encountered a fatal error.", ManagementStream);
		}
	}

	public async Task Stop() {
		using (_cts)
			await _cts.CancelAsync();

		foreach (var (index, sub) in _subscriptions) {
			try {
				Log.Verbose("Stopping custom index: {index}", index);
				await sub.Stop();
			} catch (Exception ex) {
				Log.Error(ex, "Failed to stop custom index: {index}", index);
			}
		}
	}

	private async Task StartInternal() {
		_cts.Token.ThrowIfCancellationRequested();

		await _publisher.SubscribeToStream(StreamRevision.Start, ManagementStream, _channel, ResiliencePipeline.Empty, _cts.Token);

		Dictionary<string, CreatedEvent> customIndexes = new();
		bool caughtUp = false;

		await foreach (var response in _channel.Reader.ReadAllAsync(_cts.Token)) {
			switch (response) {
				case ReadResponse.SubscriptionCaughtUp:
					if (caughtUp)
						continue;

					Log.Verbose("Subscription to: {stream} caught up", ManagementStream);

					caughtUp = true;
					foreach (var customIndex in customIndexes)
						await CreateCustomIndex(customIndex.Value);
					break;
				case ReadResponse.EventReceived eventReceived:
					var evt = eventReceived.Event;

					Log.Verbose("Subscription to: {stream} received event type: {type}", ManagementStream, evt.OriginalEvent.EventType);

					switch (evt.OriginalEvent.EventType) {
						case "$created":
							var createdEvent = JsonSerializer.Deserialize<CreatedEvent>(evt.Event.Data.Span);

							if (caughtUp)
								await CreateCustomIndex(createdEvent);
							else
								customIndexes[createdEvent.Name] = createdEvent;

							break;
						case "$deleted":
							var deletedEvent = JsonSerializer.Deserialize<DeletedEvent>(evt.Event.Data.Span);

							if (caughtUp)
								await DeleteCustomIndex(deletedEvent);
							else
								customIndexes.Remove(deletedEvent.Name);

							break;
						default:
							Log.Warning("Subscription to: {stream} received unknown event type: {type} at event number: {eventNumber}",
								ManagementStream, evt.OriginalEvent.EventType, evt.OriginalEventNumber);
							break;
					}
					break;
			}
		}
	}

	private ValueTask CreateCustomIndex(CreatedEvent createdEvent) {
		return createdEvent.PartitionKeyType switch {
			null => CreateCustomIndex<NullPartitionKey>(createdEvent),
			"number" => CreateCustomIndex<NumberPartitionKey>(createdEvent),
			"string" => CreateCustomIndex<StringPartitionKey>(createdEvent),
			"int16" => CreateCustomIndex<Int16PartitionKey>(createdEvent),
			"int32" => CreateCustomIndex<Int32PartitionKey>(createdEvent),
			"int64" => CreateCustomIndex<Int64PartitionKey>(createdEvent),
			"uint32" => CreateCustomIndex<UInt32PartitionKey>(createdEvent),
			"uint64" => CreateCustomIndex<UInt64PartitionKey>(createdEvent),
			_ => throw new ArgumentOutOfRangeException(nameof(createdEvent.PartitionKeyType))
		};
	}

	private async ValueTask CreateCustomIndex<TPartitionKey>(CreatedEvent createdEvent) where TPartitionKey : ITPartitionKey {
		Log.Debug("Creating custom index: {index}", createdEvent.Name);

		var inFlightRecords = new IndexInFlightRecords(_options);

		var indexProcessor = new CustomIndexProcessor<TPartitionKey>(
			indexName: createdEvent.Name,
			jsEventFilter: createdEvent.EventFilter,
			jsPartitionKeySelector: createdEvent.PartitionKeySelector,
			db: _db,
			inFlightRecords: inFlightRecords,
			publisher: _publisher,
			meter: _meter);

		CustomIndexSubscription subscription = new CustomIndexSubscription<TPartitionKey>(
			publisher: _publisher,
			indexProcessor: indexProcessor,
			options: _options,
			token: _cts.Token);

		_subscriptions.Add(createdEvent.Name, subscription);
		await subscription.Start();
	}

	private Task DeleteCustomIndex(DeletedEvent deletedEvent) {
		throw new NotImplementedException();
	}
}
