// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Kurrent.Surge.Resilience;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.System.Testing;
using Position = KurrentDB.Core.Services.Transport.Common.Position;
using StreamRevision = KurrentDB.Core.Services.Transport.Common.StreamRevision;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

[CollectionDefinition("SecondaryIndexingPluginDisabled")]
public sealed class SecondaryIndexingPluginDisabledDefinition : ICollectionFixture<SecondaryIndexingDisabledFixture>;

[CollectionDefinition("SecondaryIndexingPluginEnabled")]
public sealed class SecondaryIndexingPluginEnabledDefinition : ICollectionFixture<SecondaryIndexingEnabledFixture>;

[UsedImplicitly]
public class SecondaryIndexingEnabledFixture() : SecondaryIndexingFixture(true);

[UsedImplicitly]
public class SecondaryIndexingDisabledFixture() : SecondaryIndexingFixture(false);

public abstract class SecondaryIndexingFixture : ClusterVNodeFixture {
	private const string DatabasePathConfig = $"{KurrentConfigurationKeys.Prefix}:Database:Db";
	private const string PluginConfigPrefix = $"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing";
	private const string OptionsConfigPrefix = $"{PluginConfigPrefix}:Options";
	protected string? PathName;

	protected SecondaryIndexingFixture(bool isSecondaryIndexingPluginEnabled) {
		if (!isSecondaryIndexingPluginEnabled) return;

		SetUpDatabaseDirectory();

		Configuration = new() {
			{ $"{PluginConfigPrefix}:Enabled", "true" },
			{ $"{OptionsConfigPrefix}:{nameof(SecondaryIndexingPluginOptions.CommitBatchSize)}", "2" },
			{ $"{OptionsConfigPrefix}:{nameof(SecondaryIndexingPluginOptions.CommitDelayMs)}", "100" },
			{ DatabasePathConfig, PathName }
		};

		OnTearDown = CleanUpDatabaseDirectory;
	}

	public IAsyncEnumerable<ResolvedEvent> ReadStream(
		string streamName,
		CancellationToken ct = default) =>
		Publisher.ReadStream(streamName, StreamRevision.Start, long.MaxValue, true, cancellationToken: ct);

	public async IAsyncEnumerable<ResolvedEvent> SubscribeToStream(
		string streamName,
		[EnumeratorCancellation] CancellationToken ct = default
	) {
		var inboundChannel = Channel.CreateBounded<ReadResponse>(
			new BoundedChannelOptions(1000) {
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = true
			}
		);
		await Publisher.SubscribeToStream(
			StreamRevision.Start,
			streamName,
			inboundChannel,
			DefaultRetryPolicies.ConstantBackoffPipelineBuilder().Build(),
			cancellationToken: ct
		);

		await foreach (var response in inboundChannel.Reader.ReadAllAsync(ct)) {
			if (response is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;
		}
		// return inboundChannel.Reader.ReadAllAsync(ct).OfType<ReadResponse.EventReceived>().Select(e => e.Event);
	}

	public Task<List<ResolvedEvent>> ReadUntil(
		string streamName,
		Position position,
		TimeSpan? timeout = null,
		CancellationToken ct = default
	) =>
		ReadUntil(ReadStream, streamName, position, timeout, ct);

	public Task<List<ResolvedEvent>> SubscribeUntil(
		string streamName,
		Position position,
		TimeSpan? timeout = null,
		CancellationToken ct = default
	) =>
		ReadUntil(SubscribeToStream, streamName, position, timeout, ct);

	private static async Task<List<ResolvedEvent>> ReadUntil(
		Func<string, CancellationToken, IAsyncEnumerable<ResolvedEvent>> readEvents,
		string streamName,
		Position position,
		TimeSpan? timeout = null,
		CancellationToken ct = default
	) {
		timeout ??= TimeSpan.FromMilliseconds(10000);
		var endTime = DateTime.UtcNow.Add(timeout.Value);

		var events = new List<ResolvedEvent>();
		var reachedPosition = false;
		ReadResponseException.StreamNotFound? streamNotFound = null;

		try {
			CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			cts.CancelAfter(timeout.Value);

			do {
				try {
					await foreach (var resolvedEvent in readEvents(streamName, cts.Token)) {
						if (!events.Exists(e => e.Event.EventId == resolvedEvent.Event.EventId))
							events.Add(resolvedEvent);

						reachedPosition = resolvedEvent.Event.LogPosition >= (long)position.CommitPosition;

						if (reachedPosition)
							break;
					}

				} catch (ReadResponseException.StreamNotFound ex) {
					streamNotFound = ex;
				}

				if (!reachedPosition) {
					await Task.Delay(100, cts.Token);
				}
			} while (!reachedPosition && DateTime.UtcNow < endTime);

		} catch (TaskCanceledException) {
			// can happen
		}

		if (events.Count == 0 && streamNotFound != null)
			throw streamNotFound;

		return events;
	}

	public Task<WriteEventsResult> AppendToStream(string stream, params Event[] events) =>
		Publisher.WriteEvents(stream, events);

	public Task<WriteEventsResult> AppendToStream(string stream, params string[] eventData) =>
		AppendToStream(stream, eventData.Select(ToEventData).ToArray());

	public static Event ToEventData(string data) =>
		new(Guid.NewGuid(), "test", false, data, null, null);

	public static ResolvedEvent ToResolvedEvent<TLogFormat, TStreamId>(
		string stream,
		string eventType,
		string data,
		long eventNumber
	) {
		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamIdIgnored = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeIdIgnored = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		var record = new EventRecord(
			eventNumber,
			LogRecord.Prepare(recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
				streamIdIgnored, eventNumber, PrepareFlags.None, eventTypeIdIgnored, Encoding.UTF8.GetBytes(data),
				Encoding.UTF8.GetBytes("")
			),
			stream,
			eventType
		);

		return ResolvedEvent.ForUnresolvedEvent(record, 0);
	}

	void SetUpDatabaseDirectory() {
		var typeName = GetType().Name.Length > 30 ? GetType().Name[..30] : GetType().Name;
		PathName = Path.Combine(Path.GetTempPath(), $"ES-{Guid.NewGuid()}-{typeName}");

		Directory.CreateDirectory(PathName);
	}

	Task CleanUpDatabaseDirectory() =>
		PathName != null ? DirectoryDeleter.TryForceDeleteDirectoryAsync(PathName, retries: 10) : Task.CompletedTask;
}
