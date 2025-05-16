// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.SecondaryIndexing.Indices;
using KurrentDB.SecondaryIndexing.Tests.Indices;
using KurrentDB.System.Testing;
using Microsoft.Extensions.DependencyInjection;
using Position = KurrentDB.Core.Services.Transport.Common.Position;
using StreamRevision = KurrentDB.Core.Services.Transport.Common.StreamRevision;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests.Fixtures;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

[CollectionDefinition("SecondaryIndexingPluginDisabled")]
public sealed class SecondaryIndexingPluginDisabledDefinition : ICollectionFixture<SecondaryIndexingDisabledFixture>;

[CollectionDefinition("SecondaryIndexingPluginEnabled")]
public sealed class SecondaryIndexingPluginEnabledDefinition : ICollectionFixture<SecondaryIndexingEnabledFixture>;

public class SecondaryIndexingEnabledFixture() : SecondaryIndexingFixture(true);

public class SecondaryIndexingDisabledFixture() : SecondaryIndexingFixture(false);

public abstract class SecondaryIndexingFixture : ClusterVNodeFixture {
	public const string IndexStreamName = "$idx-dummy";
	private const string PluginConfigPrefix = $"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing";
	private const string OptionsConfigPrefix = $"{PluginConfigPrefix}:Options";

	protected SecondaryIndexingFixture(bool isSecondaryIndexingPluginEnabled) {
		ConfigureServices = services => {
			services.AddSingleton<ISecondaryIndex>(new FakeSecondaryIndex(IndexStreamName));
		};

		UseRandomPort = true;

		if (isSecondaryIndexingPluginEnabled) {
			Configuration = new Dictionary<string, string?> {
				{ $"{PluginConfigPrefix}:Enabled", "true" },
				{ $"{OptionsConfigPrefix}:{nameof(SecondaryIndexingPluginOptions.CheckpointCommitBatchSize)}", "2" },
				{ $"{OptionsConfigPrefix}:{nameof(SecondaryIndexingPluginOptions.CheckpointCommitDelayMs)}", "100" },
			};
		}
	}

	public IAsyncEnumerable<ResolvedEvent> ReadStream(string streamName, CancellationToken ct = default) =>
		Publisher.ReadStream(streamName, StreamRevision.Start, long.MaxValue, true, cancellationToken: ct);

	public async Task<List<ResolvedEvent>> ReadUntil(
		string streamName,
		Position position,
		TimeSpan? timeout = null,
		CancellationToken ct = default
	) {
		timeout ??= TimeSpan.FromMilliseconds(5000);
		var endTime = DateTime.UtcNow.Add(timeout.Value);

		var events = new List<ResolvedEvent>();
		var reachedPosition = false;

		ReadResponseException.StreamNotFound? streamNotFound = null;

		do {
			try {
				events = await ReadStream(streamName, ct).ToListAsync(ct);

				reachedPosition = events.Count != 0 && events.Last().Event.LogPosition <= (long)position.CommitPosition;
			} catch (ReadResponseException.StreamNotFound ex) {
				streamNotFound = ex;
			}
		} while (!reachedPosition && endTime >= DateTime.UtcNow);

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
}
