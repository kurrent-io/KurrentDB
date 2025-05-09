// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.ClientAPI.Helpers;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.SecondaryIndexing.Indices;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.SecondaryIndexing.Tests;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
[Category("SecondaryIndexing")]
public abstract class SecondaryIndexingPluginSpecification<TLogFormat, TStreamId>
	: SpecificationWithDirectoryPerTestFixture {
	private MiniNode<TLogFormat, TStreamId> _node = null!;
	private IEventStoreConnection _connection = null!;
	private TimeSpan _timeout;
	protected UserCredentials _credentials = null!;

	public abstract ISecondaryIndex Given();
	public abstract Task When();

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
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

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		_connection.Close();
		await _node.Shutdown();

		await base.TestFixtureTearDown();
	}

	private MiniNode<TLogFormat, TStreamId> CreateNode(ISecondaryIndex secondaryIndex) =>
		new(
			PathName,
			inMemDb: true,
			subsystems: [new SecondaryIndexingPlugin(secondaryIndex)]
		);

	protected Task<StreamEventsSlice> ReadStream(string stream) =>
		_connection.ReadStreamEventsForwardAsync(stream, 0, 4096, false, _credentials);

	public static ResolvedEvent CreateResolvedEvent(string stream, string eventType, string data, long eventNumber) {
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
