// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common.Log;
using EventStore.ClientAPI.Projections;
using EventStore.ClientAPI.SystemData;
using EventStore.Common.Options;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Util;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using ResolvedEvent = EventStore.ClientAPI.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.ClientAPI;

[Category("ClientAPI")]
public abstract class specification_with_standard_projections_runnning<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	protected IEventStoreConnection _conn;
	protected UserCredentials _admin = DefaultData.AdminCredentials;
	protected ProjectionsManager _manager;
	protected QueryManager _queryManager;

	private Task _projectionsCreated;
	private ProjectionsSubsystem _projections;
	private MiniNode<TLogFormat, TStreamId> _node;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		var projectionWorkerThreadCount = GivenWorkerThreadCount();
		var configuration = new ProjectionSubsystemOptions(
			projectionWorkerThreadCount,
			ProjectionType.All,
			false,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
			Opts.FaultOutOfOrderProjectionsDefault,
			500,
			250, Opts.MaxProjectionStateSizeDefault);
		_projections = new ProjectionsSubsystem(configuration);
		_node = new MiniNode<TLogFormat, TStreamId>(
			PathName, inMemDb: true,
			subsystems: [_projections]);
		_projectionsCreated = SystemProjections.Created(_projections.LeaderInputBus);

		await _node.Start();
		_conn = EventStoreConnection.Create(new ConnectionSettingsBuilder()
			.DisableServerCertificateValidation()
			.Build(), _node.TcpEndPoint);
		await _conn.ConnectAsync();

		_manager = new ProjectionsManager(
			new ConsoleLogger(),
			_node.HttpEndPoint,
			TimeSpan.FromMilliseconds(20000),
			_node.HttpMessageHandler);

		_queryManager = new QueryManager(
			new ConsoleLogger(),
			_node.HttpEndPoint,
			TimeSpan.FromMilliseconds(20000),
			TimeSpan.FromMilliseconds(20000),
			_node.HttpMessageHandler);

		WaitIdle();

		if (GivenStandardProjectionsRunning())
			await EnableStandardProjections();

		WaitIdle();
		try {
			await Given().WithTimeout(TimeSpan.FromSeconds(10));
		} catch (Exception ex) {
			throw new Exception("Given Failed", ex);
		}

		try {
			await When().WithTimeout(TimeSpan.FromSeconds(10));
		} catch (Exception ex) {
			throw new Exception("When Failed", ex);
		}
	}

	protected virtual int GivenWorkerThreadCount() {
		return 1;
	}

	[TearDown]
	public async Task PostTestAsserts() {
 			var all = await _manager.ListAllAsync(_admin);
 			if (all.Any(p => p.Name == "Faulted"))
 				Assert.Fail("Projections faulted while running the test" + "\r\n" + all);
	}

	protected async Task EnableStandardProjections() {
		await _projectionsCreated;
		await EnableProjection(ProjectionNamesBuilder.StandardProjections.EventByCategoryStandardProjection);
		await EnableProjection(ProjectionNamesBuilder.StandardProjections.EventByTypeStandardProjection);
		await EnableProjection(ProjectionNamesBuilder.StandardProjections.StreamByCategoryStandardProjection);
		await EnableProjection(ProjectionNamesBuilder.StandardProjections.StreamsStandardProjection);
	}

	protected async Task DisableStandardProjections() {
		await DisableProjection(ProjectionNamesBuilder.StandardProjections.EventByCategoryStandardProjection);
		await DisableProjection(ProjectionNamesBuilder.StandardProjections.EventByTypeStandardProjection);
		await DisableProjection(ProjectionNamesBuilder.StandardProjections.StreamByCategoryStandardProjection);
		await DisableProjection(ProjectionNamesBuilder.StandardProjections.StreamsStandardProjection);
	}

	protected virtual bool GivenStandardProjectionsRunning() {
		return true;
	}

	protected Task EnableProjection(string name) {
		return _manager.EnableAsync(name, _admin);
	}

	protected Task DisableProjection(string name) {
		return _manager.DisableAsync(name, _admin);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		if (_conn != null)
			_conn.Close();

		if (_node != null)
			await _node.Shutdown();

		await base.TestFixtureTearDown();
	}

	protected virtual Task When() => Task.CompletedTask;

	protected virtual Task Given() => Task.CompletedTask;

	protected Task PostEvent(string stream, string eventType, string data) {
		return _conn.AppendToStreamAsync(stream, ExpectedVersion.Any, CreateEvent(eventType, data));
	}

	protected Task HardDeleteStream(string stream) {
		return _conn.DeleteStreamAsync(stream, ExpectedVersion.Any, true, _admin);
	}

	protected Task SoftDeleteStream(string stream) {
		return _conn.DeleteStreamAsync(stream, ExpectedVersion.Any, false, _admin);
	}

	protected static EventData CreateEvent(string type, string data) {
		return new EventData(Guid.NewGuid(), type, true, Encoding.UTF8.GetBytes(data), new byte[0]);
	}

	protected void WaitIdle(int multiplier = 1) {
#if DEBUG
		_node.WaitIdle();
#endif
	}

#pragma warning disable 1998
	protected async Task AssertStreamTail(string streamId, params string[] events) {
#pragma warning restore 1998
#if DEBUG
		await Task.Delay(TimeSpan.FromMilliseconds(500));
		var result = await _conn.ReadStreamEventsBackwardAsync(streamId, -1, events.Length, true, _admin);
		switch (result.Status) {
			case SliceReadStatus.StreamDeleted:
				Assert.Fail("Stream '{0}' is deleted", streamId);
				break;
			case SliceReadStatus.StreamNotFound:
				Assert.Fail("Stream '{0}' does not exist", streamId);
				break;
			case SliceReadStatus.Success:
				var resultEventsReversed = result.Events.Reverse().ToArray();
				if (resultEventsReversed.Length < events.Length)
					DumpFailed("Stream does not contain enough events", streamId, events, result.Events);
				else {
					for (var index = 0; index < events.Length; index++) {
						var parts = events[index].Split(new char[] {':'}, 2);
						var eventType = parts[0];
						var eventData = parts[1];

						if (resultEventsReversed[index].Event.EventType != eventType)
							DumpFailed("Invalid event type", streamId, events, resultEventsReversed);
						else if (resultEventsReversed[index].Event.DebugDataView() != eventData)
							DumpFailed("Invalid event body", streamId, events, resultEventsReversed);
					}
				}

				break;
		}
#endif
	}

#pragma warning disable 1998
	protected async Task DumpStream(string streamId) {
#pragma warning restore 1998
#if DEBUG
		var result = await _conn.ReadStreamEventsBackwardAsync(streamId, -1, 100, true, _admin);
		switch (result.Status) {
			case SliceReadStatus.StreamDeleted:
				Assert.Fail("Stream '{0}' is deleted", streamId);
				break;
			case SliceReadStatus.StreamNotFound:
				Assert.Fail("Stream '{0}' does not exist", streamId);
				break;
			case SliceReadStatus.Success:
				Dump("Dumping..", streamId, result.Events.Reverse().ToArray());
				break;
		}
#endif
	}

#if DEBUG
	private void DumpFailed(string message, string streamId, string[] events, ResolvedEvent[] resultEvents) {
		var expected = events.Aggregate("", (a, v) => a + ", " + v);
		var actual = resultEvents.Aggregate(
			"", (a, v) => a + ", " + v.Event.EventType + ":" + v.Event.DebugDataView());

		var actualMeta = resultEvents.Aggregate(
			"", (a, v) => a + "\r\n" + v.Event.EventType + ":" + v.Event.DebugMetadataView());


		Assert.Fail(
			"Stream: '{0}'\r\n{1}\r\n\r\nExisting events: \r\n{2}\r\n Expected events: \r\n{3}\r\n\r\nActual metas:{4}",
			streamId,
			message, actual, expected, actualMeta);
	}

	protected void Dump(string message, string streamId, ResolvedEvent[] resultEvents) {
		var actual = resultEvents.Aggregate(
			"", (a, v) => a + ", " + v.OriginalEvent.EventType + ":" + v.OriginalEvent.DebugDataView());

		var actualMeta = resultEvents.Aggregate(
			"", (a, v) => a + "\r\n" + v.OriginalEvent.EventType + ":" + v.OriginalEvent.DebugMetadataView());


		Debug.WriteLine(
			"Stream: '{0}'\r\n{1}\r\n\r\nExisting events: \r\n{2}\r\n \r\nActual metas:{3}", streamId,
			message, actual, actualMeta);
	}
#endif

	protected async Task PostProjection(string query) {
		await _manager.CreateContinuousAsync("test-projection", query, _admin);
		WaitIdle();
	}

	protected async Task PostQuery(string query) {
		await _manager.CreateTransientAsync("query", query, _admin);
		WaitIdle();
	}
}
