// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.ClientAPI.Helpers;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.SecondaryIndexing.Indices;
using Microsoft.Extensions.Configuration;
using ExpectedVersion = KurrentDB.Core.Data.ExpectedVersion;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;
using ClientResolvedEvent = EventStore.ClientAPI.ResolvedEvent;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

public abstract class SecondaryIndexingPluginSpecification<TLogFormat, TStreamId>
	: SpecificationWithDirectoryPerTestFixture, IAsyncLifetime {
	private MiniNode<TLogFormat, TStreamId> _node = null!;
	private IEventStoreConnection _connection = null!;
	private TimeSpan _timeout;
	protected UserCredentials _credentials = null!;
	protected bool IsPluginEnabled;

	protected abstract ISecondaryIndex Given();
	protected abstract Task When();

	public async Task InitializeAsync() {
		await base.TestFixtureSetUp();

		ISecondaryIndex secondaryIndex = Given();

		_credentials = new UserCredentials(SystemUsers.Admin, SystemUsers.DefaultAdminPassword);
		_timeout = TimeSpan.FromSeconds(20);
		_node = CreateNode(secondaryIndex);
		await _node.Start().WithTimeout(_timeout);

		_connection = TestConnection.Create(_node.TcpEndPoint);
		await _connection.ConnectAsync();

		try {
			await When().WithTimeout(_timeout);
		} catch (Exception ex) {
			throw new Exception("When Failed", ex);
		}
	}

	public async Task DisposeAsync() {
		_connection.Close();
		await _node.Shutdown();

		await base.TestFixtureTearDown();
	}

	private MiniNode<TLogFormat, TStreamId> CreateNode(ISecondaryIndex secondaryIndex) =>
		new(
			PathName,
			inMemDb: true,
			subsystems: [new SecondaryIndexingPlugin<TStreamId>(secondaryIndex)],
			configuration: IsPluginEnabled
				? new ConfigurationBuilder()
					.AddInMemoryCollection(new Dictionary<string, string?> {
						{ $"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Enabled", "true" }
					}).Build()
				: null
		);

	protected async Task<ClientResolvedEvent[]> ReadStream(string streamName) =>
		(await _connection.ReadStreamEventsForwardAsync(streamName, 0, 4096, false, _credentials)).Events;

	protected async Task<ClientResolvedEvent[]> ReadUntil(
		string streamName,
		Position? position,
		TimeSpan? timeout = null
	) {
		ClientResolvedEvent[] events = [];
		var startTime = DateTime.UtcNow;
		timeout ??= TimeSpan.FromSeconds(1000250);

		do {
			if (DateTime.UtcNow - startTime > timeout)
				break;

			var readEvents = await ReadStream(streamName);
			events = readEvents;
		} while (!events.Any(e => e.OriginalPosition >= position));

		return events;
	}

	protected Task<WriteResult> AppendToStream(string stream, params EventData[] events) =>
		_connection.AppendToStreamAsync(stream, ExpectedVersion.Any, events);

	protected Task<WriteResult> AppendToStream(string stream, params string[] eventData) =>
		AppendToStream(stream, eventData.Select(ToEventData).ToArray());

	protected static EventData ToEventData(string data) =>
		new(
			Guid.NewGuid(),
			"test",
			false,
			Encoding.UTF8.GetBytes(data), []
		);

	protected static ResolvedEvent ToResolvedEvent(string stream, string eventType, string data, long eventNumber) {
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
