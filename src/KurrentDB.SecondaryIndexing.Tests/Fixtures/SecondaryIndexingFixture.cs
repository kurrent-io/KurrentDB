// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Text;
using EventStore.Plugins.Licensing;
using KurrentDB.Core;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.Tests;
using KurrentDB.Surge.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Position = KurrentDB.Core.Services.Transport.Common.Position;
using StreamRevision = KurrentDB.Core.Services.Transport.Common.StreamRevision;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

[UsedImplicitly]
public class SecondaryIndexingEnabledFixture() : SecondaryIndexingFixture(true);

[UsedImplicitly]
public class SecondaryIndexingDisabledFixture() : SecondaryIndexingFixture(false);

public abstract class SecondaryIndexingFixture : ClusterVNodeFixture {
	private const string DatabasePathConfig = $"{KurrentConfigurationKeys.Prefix}:Database:Db";
	private const string PluginConfigPrefix = $"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing";
	private const string OptionsConfigPrefix = $"{PluginConfigPrefix}:Options";
	private const string ProjectionsConfigPrefix = $"{KurrentConfigurationKeys.Prefix}:Projections";

	private readonly TimeSpan _defaultTimeout = TimeSpan.FromMilliseconds(3000);
	private string? _path;

	public readonly int CommitSize = 500;

	protected SecondaryIndexingFixture(bool isSecondaryIndexingPluginEnabled) {
		if (!isSecondaryIndexingPluginEnabled)
			return;

		SetUpDatabaseDirectory();

		Configuration = new() {
			{ $"{PluginConfigPrefix}:Enabled", "true" },
			{ $"{OptionsConfigPrefix}:{nameof(SecondaryIndexingPluginOptions.CommitBatchSize)}", CommitSize.ToString() },
			{ DatabasePathConfig, _path },
			{ $"{ProjectionsConfigPrefix}:RunProjections", "None" }
		};

		ConfigureServices = services => {
			services.RemoveAll<ILicenseService>();
			services.AddSingleton<ILicenseService>(_ => new FakeLicenseService());
		};

		OnTearDown = CleanUpDatabaseDirectory;
	}

	public async Task<List<ResolvedEvent>> ReadUntil(string indexName, int maxCount, bool forwards, Position? from = null, TimeSpan? timeout = null, CancellationToken ct = default) {
		timeout ??= _defaultTimeout;
		var endTime = DateTime.UtcNow.Add(timeout.Value);

		var events = new List<ResolvedEvent>();
		ReadResponseException.IndexNotFound? indexNotFound = null;

		CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeout.Value);

		do {
			try {
				var start = from ?? (forwards ? Position.Start : Position.End);
				events = await Publisher.ReadIndex(indexName, start, maxCount, forwards: forwards, cancellationToken: cts.Token).ToListAsync(cts.Token);

				if (events.Count != maxCount) {
					await Task.Delay(25, cts.Token);
				}
			} catch (ReadResponseException.IndexNotFound ex) {
				indexNotFound = ex;
				break;
			} catch (OperationCanceledException) {
				break;
			}
		} while (events.Count != maxCount && DateTime.UtcNow < endTime);

		if (events.Count == 0 && indexNotFound != null)
			throw indexNotFound;

		return events;
	}

	public async Task<List<ResolvedEvent>> SubscribeUntil(string indexName, int maxCount, TimeSpan? timeout = null, CancellationToken ct = default) {
		timeout ??= _defaultTimeout;

		var events = new List<ResolvedEvent>();

		CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeout.Value);

		try {
			await foreach (var evt in SubscribeToIndex(indexName, maxCount, cts.Token)) {
				events.Add(evt);
			}
		} catch (OperationCanceledException) {
			// can happen
		}

		return events;
	}

	private async IAsyncEnumerable<ResolvedEvent> SubscribeToIndex(string indexName, int maxCount, [EnumeratorCancellation] CancellationToken ct = default) {
		var enumerable = Publisher.SubscribeToIndex(indexName, Position.Start, cancellationToken: ct);

		int count = 0;

		await foreach (var response in enumerable) {
			if (count == maxCount)
				yield break;

			if (response is not ReadResponse.EventReceived eventReceived)
				continue;

			count++;
			yield return eventReceived.Event;
		}
	}

	public Task<WriteEventsResult> AppendToStream(string stream, params Event[] events) =>
		Publisher.WriteEvents(stream, events, requireLeader: false, SystemAccounts.System);


	public Task<WriteEventsResult> DeleteStream(string stream) =>
		Publisher.DeleteStream(stream);


	public Task<WriteEventsResult> HardDeleteStream(string stream) =>
		Publisher.HardDeleteStream(stream);


	public Task<WriteEventsResult> AppendToStream(string stream, params string[] eventData) =>
		AppendToStream(stream, eventData.Select(ToEventData).ToArray());

	public static Event ToEventData(string data) => new(Guid.NewGuid(), "test", false, Encoding.UTF8.GetBytes(data), false, []);

	public async Task<ClientMessage.CreatePersistentSubscriptionToIndexCompleted> CreatePersistentSubscriptionToIndex(
		string indexName, string group, TFPos? startFrom = null,
		int readBatchSize = 20, int bufferSize = 500, int liveBufferSize = 500,
		int minCheckPointCount = 5, int maxCheckPointCount = 1000,
		int checkPointAfterMilliseconds = 1000, int messageTimeoutMilliseconds = 30000,
		CancellationToken ct = default) {
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.CreatePersistentSubscriptionToIndexCompleted>();

		Publisher.Publish(new ClientMessage.CreatePersistentSubscriptionToIndex(
			internalCorrId: corrId,
			correlationId: corrId,
			envelope: envelope,
			groupName: group,
			indexName: indexName,
			resolveLinkTos: false,
			startFrom: startFrom ?? new TFPos(0, 0),
			messageTimeoutMilliseconds: messageTimeoutMilliseconds,
			recordStatistics: false,
			maxRetryCount: 10,
			bufferSize: bufferSize,
			liveBufferSize: liveBufferSize,
			readBatchSize: readBatchSize,
			checkPointAfterMilliseconds: checkPointAfterMilliseconds,
			minCheckPointCount: minCheckPointCount,
			maxCheckPointCount: maxCheckPointCount,
			maxSubscriberCount: 0,
			namedConsumerStrategy: SystemConsumerStrategies.RoundRobin,
			user: SystemAccounts.System));

		return await envelope.Task.WaitAsync(ct);
	}

	/// <summary>
	/// Connects to a persistent subscription on an index and returns received events.
	/// Collects events until <paramref name="maxCount"/> are received or the token is cancelled.
	/// </summary>
	public async Task<List<ResolvedEvent>> ConnectToPersistentSubscriptionToIndex(
		string indexName, string group, int maxCount, CancellationToken ct = default) {
		var corrId = Guid.NewGuid();
		var events = new List<ResolvedEvent>();
		string? subscriptionId = null;
		var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var done = new TaskCompletionSource<List<ResolvedEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);

		var semaphore = new SemaphoreSlim(1, 1);
		var envelope = new ContinuationEnvelope(OnMessage, semaphore, ct);

		Publisher.Publish(new ClientMessage.ConnectToPersistentSubscriptionToIndex(
			internalCorrId: corrId,
			correlationId: corrId,
			envelope: envelope,
			connectionId: corrId,
			connectionName: "test-connection",
			groupName: group,
			indexName: indexName,
			allowedInFlightMessages: 10,
			from: "",
			user: SystemAccounts.System));

		await confirmed.Task.WaitAsync(ct);
		return await done.Task.WaitAsync(ct);

		Task OnMessage(Message msg, CancellationToken token) {
			switch (msg) {
				case ClientMessage.PersistentSubscriptionConfirmation confirmation:
					subscriptionId = confirmation.SubscriptionId;
					confirmed.TrySetResult();
					break;
				case ClientMessage.PersistentSubscriptionStreamEventAppeared appeared:
					events.Add(appeared.Event);
					Publisher.Publish(new ClientMessage.PersistentSubscriptionAckEvents(
						corrId, corrId, new NoopEnvelope(),
						subscriptionId,
						[appeared.Event.OriginalEvent.EventId],
						SystemAccounts.System));
					if (events.Count >= maxCount)
						done.TrySetResult(events.ToList());
					break;
				case ClientMessage.SubscriptionDropped dropped:
					done.TrySetException(new Exception($"Subscription dropped: {dropped.Reason}"));
					confirmed.TrySetException(new Exception($"Subscription dropped: {dropped.Reason}"));
					break;
			}
			return Task.CompletedTask;
		}
	}

	/// <summary>
	/// Connects, collects up to <paramref name="maxCount"/> events, then stops acking
	/// further events. Returns collected events and the correlation ID for unsubscribing.
	/// </summary>
	public async Task<(List<ResolvedEvent> Events, Guid CorrelationId)> ConnectAndCollectFromIndex(
		string indexName, string group, int maxCount, CancellationToken ct = default) {
		var corrId = Guid.NewGuid();
		var events = new List<ResolvedEvent>();
		string? subscriptionId = null;
		var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var done = new TaskCompletionSource<List<ResolvedEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);

		var semaphore = new SemaphoreSlim(1, 1);
		var envelope = new ContinuationEnvelope(OnMessage, semaphore, ct);

		Publisher.Publish(new ClientMessage.ConnectToPersistentSubscriptionToIndex(
			internalCorrId: corrId,
			correlationId: corrId,
			envelope: envelope,
			connectionId: corrId,
			connectionName: "test-connection",
			groupName: group,
			indexName: indexName,
			allowedInFlightMessages: 10,
			from: "",
			user: SystemAccounts.System));

		await confirmed.Task.WaitAsync(ct);
		var result = await done.Task.WaitAsync(ct);
		return (result, corrId);

		Task OnMessage(Message msg, CancellationToken token) {
			switch (msg) {
				case ClientMessage.PersistentSubscriptionConfirmation confirmation:
					subscriptionId = confirmation.SubscriptionId;
					confirmed.TrySetResult();
					break;
				case ClientMessage.PersistentSubscriptionStreamEventAppeared appeared
					when events.Count < maxCount:
					events.Add(appeared.Event);
					Publisher.Publish(new ClientMessage.PersistentSubscriptionAckEvents(
						corrId, corrId, new NoopEnvelope(),
						subscriptionId,
						[appeared.Event.OriginalEvent.EventId],
						SystemAccounts.System));
					if (events.Count >= maxCount)
						done.TrySetResult(events.ToList());
					break;
				case ClientMessage.SubscriptionDropped dropped:
					done.TrySetException(new Exception($"Subscription dropped: {dropped.Reason}"));
					confirmed.TrySetException(new Exception($"Subscription dropped: {dropped.Reason}"));
					break;
			}
			return Task.CompletedTask;
		}
	}

	public async Task<ClientMessage.DeletePersistentSubscriptionToIndexCompleted> DeletePersistentSubscriptionToIndex(
		string indexName, string group, CancellationToken ct = default) {
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.DeletePersistentSubscriptionToIndexCompleted>();

		Publisher.Publish(new ClientMessage.DeletePersistentSubscriptionToIndex(
			internalCorrId: corrId,
			correlationId: corrId,
			envelope: envelope,
			indexName: indexName,
			groupName: group,
			user: SystemAccounts.System));

		return await envelope.Task.WaitAsync(ct);
	}

	private void SetUpDatabaseDirectory() {
		var typeName = GetType().Name.Length > 30 ? GetType().Name[..30] : GetType().Name;
		_path = Path.Combine(Path.GetTempPath(), $"ES-{Guid.NewGuid()}-{typeName}");

		Directory.CreateDirectory(_path);
	}

	private Task CleanUpDatabaseDirectory() =>
		_path != null ? DirectoryDeleter.TryForceDeleteDirectoryAsync(_path, retries: 10) : Task.CompletedTask;
}
