// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.SecondaryIndexing.Indices;
using KurrentDB.SecondaryIndexing.Tests.Indices;
using KurrentDB.SecondaryIndexing.Tests.IntegrationTests.Fixtures;
using KurrentDB.Surge.Testing.Xunit.Extensions.AssemblyFixture;
using KurrentDB.System.Testing;
using Microsoft.Extensions.DependencyInjection;
using Position = KurrentDB.Core.Services.Transport.Common.Position;
using StreamRevision = KurrentDB.Core.Services.Transport.Common.StreamRevision;

[assembly:
	TestFramework(XunitTestFrameworkWithAssemblyFixture.TypeName, XunitTestFrameworkWithAssemblyFixture.AssemblyName)]
[assembly: AssemblyFixture(typeof(SecondaryIndexingEnabledAssemblyFixture))]
//[assembly: AssemblyFixture(typeof(SecondaryIndexingDisabledAssemblyFixture))]

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests.Fixtures;

using WriteEventsResult = (Position Position, StreamRevision StreamRevision);

public class SecondaryIndexingEnabledAssemblyFixture() : SecondaryIndexingFixture(true);

public class SecondaryIndexingDisabledAssemblyFixture() : SecondaryIndexingFixture(false);

public abstract class SecondaryIndexingFixture : ClusterVNodeFixture {
	public const string IndexStreamName = "$idx-dummy";

	protected SecondaryIndexingFixture(bool isSecondaryIndexingPluginEnabled) {
		ConfigureServices = services => {
			services.AddSingleton<ISecondaryIndex>(new FakeSecondaryIndex(IndexStreamName));
		};

		if (isSecondaryIndexingPluginEnabled)
			Configuration = new Dictionary<string, string?> {
				{ $"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Enabled", "true" }
			};
	}

	public IAsyncEnumerable<ResolvedEvent> ReadStream(string streamName, CancellationToken ct = default) =>
		Publisher.ReadStream(streamName, StreamRevision.Start, long.MaxValue, true, cancellationToken: ct);

	public IAsyncEnumerable<ResolvedEvent> ReadUntil(
		string streamName,
		Position position,
		TimeSpan? timeout = null,
		CancellationToken ct = default
	) {
		var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(1000250));

		return ReadStream(streamName, cts.Token)
			.Where(e => e.Event.LogPosition <= (long)position.CommitPosition);
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
