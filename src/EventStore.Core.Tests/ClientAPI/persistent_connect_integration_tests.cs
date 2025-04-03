// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class happy_case_writing_and_subscribing_to_normal_events_manual_ack<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private const int BufferCount = 10;
	private const int EventWriteCount = BufferCount * 2;

	private readonly ManualResetEvent _eventsReceived = new ManualResetEvent(false);
	private int _eventReceivedCount;

	protected override Task When() => Task.WhenAll(_node.Started, _node.AdminUserCreated);

	[Test]
	public async Task Test() {
		var streamName = Guid.NewGuid().ToString();
		var groupName = Guid.NewGuid().ToString();
		var settings = PersistentSubscriptionSettings
			.Create()
			.StartFromCurrent()
			.ResolveLinkTos()
			.Build();

		await _conn.CreatePersistentSubscriptionAsync(streamName, groupName, settings, DefaultData.AdminCredentials);
		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName,
			(subscription, resolvedEvent) => {
				subscription.Acknowledge(resolvedEvent);

				if (Interlocked.Increment(ref _eventReceivedCount) == EventWriteCount) {
					_eventsReceived.Set();
				}

				return Task.CompletedTask;
			},
			(sub, reason, exception) =>
				Console.WriteLine("Subscription dropped (reason:{0}, exception:{1}).", reason, exception),
			bufferSize: 10, autoAck: false, userCredentials: DefaultData.AdminCredentials);

		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);

			await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, eventData);
		}

		if (!_eventsReceived.WaitOne(TimeSpan.FromSeconds(5))) {
			throw new Exception("Timed out waiting for events.");
		}
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class happy_case_writing_and_subscribing_to_normal_events_auto_ack<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private const int BufferCount = 10;
	private const int EventWriteCount = BufferCount * 2;

	private readonly ManualResetEvent _eventsReceived = new ManualResetEvent(false);
	private int _eventReceivedCount;

	protected override Task When() => Task.WhenAll(_node.Started, _node.AdminUserCreated);

	[Test]
	public async Task Test() {
		var streamName = Guid.NewGuid().ToString();
		var groupName = Guid.NewGuid().ToString();
		var settings = PersistentSubscriptionSettings
			.Create()
			.StartFromCurrent()
			.ResolveLinkTos()
			.Build();
		await _conn.CreatePersistentSubscriptionAsync(streamName, groupName, settings, DefaultData.AdminCredentials)
;
		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName,
			(subscription, resolvedEvent) => {
				if (Interlocked.Increment(ref _eventReceivedCount) == EventWriteCount) {
					_eventsReceived.Set();
				}

				return Task.CompletedTask;
			},
			(sub, reason, exception) =>
				Console.WriteLine("Subscription dropped (reason:{0}, exception:{1}).", reason, exception),
			userCredentials: DefaultData.AdminCredentials);

		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);

			await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, eventData);
		}

		if (!_eventsReceived.WaitOne(TimeSpan.FromSeconds(15))) {
			throw new Exception($"Timed out waiting for events, received {_eventReceivedCount} event(s).");
		}
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class happy_case_catching_up_to_normal_events_auto_ack<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private const int BufferCount = 10;
	private const int EventWriteCount = BufferCount * 2;

	private readonly ManualResetEvent _eventsReceived = new ManualResetEvent(false);
	private int _eventReceivedCount;

	protected override Task When() => Task.WhenAll(_node.Started, _node.AdminUserCreated);


	[Test]
	public async Task Test() {
		var streamName = Guid.NewGuid().ToString();
		var groupName = Guid.NewGuid().ToString();
		var settings = PersistentSubscriptionSettings
			.Create()
			.StartFromBeginning()
			.ResolveLinkTos()
			.Build();
		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);

			await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, eventData);
		}

		await _conn.CreatePersistentSubscriptionAsync(streamName, groupName, settings, DefaultData.AdminCredentials);
		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName,
			(subscription, resolvedEvent) => {
				if (Interlocked.Increment(ref _eventReceivedCount) == EventWriteCount) {
					_eventsReceived.Set();
				}

				return Task.CompletedTask;
			},
			(sub, reason, exception) => {
				Console.WriteLine($"Subscription dropped (reason:{reason}, exception:{exception}).");
			},
			userCredentials: DefaultData.AdminCredentials,
			autoAck: true);

		if (!_eventsReceived.WaitOne(TimeSpan.FromSeconds(5))) {
			throw new Exception("Timed out waiting for events.");
		}
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class happy_case_catching_up_to_normal_events_manual_ack<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private const int BufferCount = 10;
	private const int EventWriteCount = BufferCount * 2;

	private readonly ManualResetEvent _eventsReceived = new ManualResetEvent(false);
	private int _eventReceivedCount;

	protected override Task When() => Task.WhenAll(_node.Started, _node.AdminUserCreated);


	[Test]
	public async Task Test() {
		var streamName = Guid.NewGuid().ToString();
		var groupName = Guid.NewGuid().ToString();
		var settings = PersistentSubscriptionSettings
			.Create()
			.StartFromBeginning()
			.ResolveLinkTos()
			.Build();
		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);

			await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, eventData);
		}

		await _conn.CreatePersistentSubscriptionAsync(streamName, groupName, settings, DefaultData.AdminCredentials);
		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName,
			(subscription, resolvedEvent) => {
				subscription.Acknowledge(resolvedEvent);
				if (Interlocked.Increment(ref _eventReceivedCount) == EventWriteCount) {
					_eventsReceived.Set();
				}

				return Task.CompletedTask;
			},
			(sub, reason, exception) =>
				Console.WriteLine("Subscription dropped (reason:{0}, exception:{1}).", reason, exception),
			userCredentials: DefaultData.AdminCredentials,
			autoAck: false);

		if (!_eventsReceived.WaitOne(TimeSpan.FromSeconds(5))) {
			throw new Exception("Timed out waiting for events.");
		}
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class happy_case_catching_up_to_link_to_events_manual_ack<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private const int BufferCount = 10;
	private const int EventWriteCount = BufferCount * 2;

	private readonly ManualResetEvent _eventsReceived = new ManualResetEvent(false);
	private int _eventReceivedCount;

	protected override Task When() => Task.WhenAll(_node.Started, _node.AdminUserCreated);

	[Test]
	public async Task Test() {
		var streamName = Guid.NewGuid().ToString();
		var groupName = Guid.NewGuid().ToString();
		var settings = PersistentSubscriptionSettings
			.Create()
			.StartFromBeginning()
			.ResolveLinkTos()
			.Build();
		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);
			await _conn.AppendToStreamAsync(streamName + "original", ExpectedVersion.Any, DefaultData.AdminCredentials,
				eventData);
		}

		await _conn.CreatePersistentSubscriptionAsync(streamName, groupName, settings, DefaultData.AdminCredentials);
		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName,
			(subscription, resolvedEvent) => {
				subscription.Acknowledge(resolvedEvent);
				if (Interlocked.Increment(ref _eventReceivedCount) == EventWriteCount) {
					_eventsReceived.Set();
				}

				return Task.CompletedTask;
			},
			(sub, reason, exception) =>
				Console.WriteLine("Subscription dropped (reason:{0}, exception:{1}).", reason, exception),
			userCredentials: DefaultData.AdminCredentials,
			autoAck: false);
		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), SystemEventTypes.LinkTo, false,
				Encoding.UTF8.GetBytes(i + "@" + streamName + "original"), null);
			await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, eventData);
		}

		if (!_eventsReceived.WaitOne(TimeSpan.FromSeconds(5))) {
			throw new Exception("Timed out waiting for events.");
		}
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class happy_case_catching_up_to_link_to_events_auto_ack<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private const int BufferCount = 10;
	private const int EventWriteCount = BufferCount * 2;

	private readonly ManualResetEvent _eventsReceived = new ManualResetEvent(false);
	private int _eventReceivedCount;

	protected override Task When() => Task.WhenAll(_node.Started, _node.AdminUserCreated);

	[Test]
	public async Task Test() {
		var streamName = Guid.NewGuid().ToString();
		var groupName = Guid.NewGuid().ToString();
		var settings = PersistentSubscriptionSettings
			.Create()
			.StartFromBeginning()
			.ResolveLinkTos()
			.Build();
		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);
			await _conn.AppendToStreamAsync(streamName + "original", ExpectedVersion.Any, DefaultData.AdminCredentials,
				eventData);
		}

		await _conn.CreatePersistentSubscriptionAsync(streamName, groupName, settings, DefaultData.AdminCredentials);
		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName,
			(subscription, resolvedEvent) => {
				if (Interlocked.Increment(ref _eventReceivedCount) == EventWriteCount) {
					_eventsReceived.Set();
				}

				return Task.CompletedTask;
			},
			(sub, reason, exception) =>
				Console.WriteLine("Subscription dropped (reason:{0}, exception:{1}).", reason, exception),
			userCredentials: DefaultData.AdminCredentials,
			autoAck: true);
		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), SystemEventTypes.LinkTo, false,
				Encoding.UTF8.GetBytes(i + "@" + streamName + "original"), null);
			await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, eventData);
		}

		if (!_eventsReceived.WaitOne(TimeSpan.FromSeconds(5))) {
			throw new Exception("Timed out waiting for events.");
		}
	}
}

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_and_subscribing_to_normal_events_manual_nack<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private const int BufferCount = 10;
	private const int EventWriteCount = BufferCount * 2;

	private readonly ManualResetEvent _eventsReceived = new ManualResetEvent(false);
	private int _eventReceivedCount;

	protected override Task When() => Task.WhenAll(_node.Started, _node.AdminUserCreated);

	[Test]
	public async Task Test() {
		var streamName = Guid.NewGuid().ToString();
		var groupName = Guid.NewGuid().ToString();
		var settings = PersistentSubscriptionSettings
			.Create()
			.StartFromCurrent()
			.ResolveLinkTos()
			.Build();

		await _conn.CreatePersistentSubscriptionAsync(streamName, groupName, settings, DefaultData.AdminCredentials);
		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName,
			(subscription, resolvedEvent) => {
				subscription.Fail(resolvedEvent, PersistentSubscriptionNakEventAction.Park, "fail");

				if (Interlocked.Increment(ref _eventReceivedCount) == EventWriteCount) {
					_eventsReceived.Set();
				}

				return Task.CompletedTask;
			},
			(sub, reason, exception) =>
				Console.WriteLine("Subscription dropped (reason:{0}, exception:{1}).", reason, exception),
			bufferSize: 10, autoAck: false, userCredentials: DefaultData.AdminCredentials);

		for (var i = 0; i < EventWriteCount; i++) {
			var eventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);

			await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, eventData);
		}

		if (!_eventsReceived.WaitOne(TimeSpan.FromSeconds(5))) {
			throw new Exception("Timed out waiting for events.");
		}
	}
}

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_connection_drops_messages_that_have_run_out_of_retries_are_not_retried<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly TaskCompletionSource<bool> _subscriptionDropped = new TaskCompletionSource<bool>();
	private readonly TaskCompletionSource<bool> _eventReceived = new TaskCompletionSource<bool>();
	private ResolvedEvent _receivedEvent;

	protected override Task When() => Task.WhenAll(_node.Started, _node.AdminUserCreated);

	[Test]
	[Retry(10)]
	public void Test() => Assert.DoesNotThrowAsync(async () => {
		await CloseConnectionAndWait(_conn);
		_conn = BuildConnection(_node);
		AddLogging(_conn);
		await _conn.ConnectAsync();
		
		var streamName = Guid.NewGuid().ToString();
		var groupName = Guid.NewGuid().ToString();
		var settings = PersistentSubscriptionSettings
			.Create()
			.StartFromCurrent()
			.WithMaxRetriesOf(0) // Don't retry messages
			.Build();

		await _conn.CreatePersistentSubscriptionAsync(streamName, groupName, settings, DefaultData.AdminCredentials);
		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName, async (subscription, resolvedEvent) => {
				await CloseConnectionAndWait(_conn);
			},
			(sub, reason, exception) => {
				Console.WriteLine("Subscription dropped (reason:{0}, exception:{1}). @ {2}", reason, exception, DateTime.Now);
				_subscriptionDropped.TrySetResult(true);
			},
			bufferSize: 10, autoAck: false, userCredentials: DefaultData.AdminCredentials);

		var parkedEventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);
		await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, parkedEventData);

		await _subscriptionDropped.Task.WithTimeout();

		_conn = BuildConnection(_node);
		AddLogging(_conn);
		await _conn.ConnectAsync();

		await _conn.ConnectToPersistentSubscriptionAsync(streamName, groupName, (subscription, resolvedEvent) => {
			subscription.Acknowledge(resolvedEvent);
			_receivedEvent = resolvedEvent;
			_eventReceived.TrySetResult(true);
		}, (sub, reason, exception) => {
			Console.WriteLine("Second Subscription dropped (reason:{0}, exception:{1}).", reason, exception);
		}, bufferSize: 10, autoAck: false, userCredentials: DefaultData.AdminCredentials);

		// Ensure we only get the new event, not the previous one
		var newEventData = new EventData(Guid.NewGuid(), "SomeEvent", false, new byte[0], new byte[0]);
		await _conn.AppendToStreamAsync(streamName, ExpectedVersion.Any, DefaultData.AdminCredentials, newEventData);

		await _eventReceived.Task.WithTimeout();
		Assert.AreEqual(newEventData.EventId, _receivedEvent.Event.EventId);
		
		//flaky: temporarily added for debugging
		void AddLogging(IEventStoreConnection conn) {
			conn.AuthenticationFailed += (_, args) => Console.WriteLine($"_conn.AuthenticationFailed: {args.Connection.ConnectionName} @ {DateTime.Now} {TestContext.CurrentContext.CurrentRepeatCount}");
			conn.Closed += (_, args) => Console.WriteLine($"_conn.Closed: {args.Connection.ConnectionName} @ {DateTime.Now} {TestContext.CurrentContext.CurrentRepeatCount}");
			conn.Connected += (_, args) => Console.WriteLine($"_conn.Connected: {args.Connection.ConnectionName} @ {DateTime.Now} {TestContext.CurrentContext.CurrentRepeatCount}");
			conn.Disconnected += (_, args) => Console.WriteLine($"_conn.Disconnected: {args.Connection.ConnectionName} @ {DateTime.Now} {TestContext.CurrentContext.CurrentRepeatCount}");
			conn.ErrorOccurred += (_, args) => Console.WriteLine($"_conn.ErrorOccurred: {args.Connection.ConnectionName} @ {DateTime.Now} {TestContext.CurrentContext.CurrentRepeatCount}");
			conn.Reconnecting += (_, args) => Console.WriteLine($"_conn.Reconnecting: {args.Connection.ConnectionName} @ {DateTime.Now} {TestContext.CurrentContext.CurrentRepeatCount}");
		}
	});
}
