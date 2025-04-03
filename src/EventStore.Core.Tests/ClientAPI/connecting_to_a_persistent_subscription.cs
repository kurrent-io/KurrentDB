// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using EventStore.ClientAPI;
using EventStore.ClientAPI.ClientOperations;
using EventStore.ClientAPI.Exceptions;
using NUnit.Framework;
using EventStore.ClientAPI.Common;
using EventStore.ClientAPI.Common.Utils;
using System.Threading.Tasks;

namespace EventStore.Core.Tests.ClientAPI;

[Category("LongRunning"), Category("ClientAPI")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class connect_to_non_existing_persistent_subscription_with_permissions<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private Exception _caught;

	protected override Task When() {
		_caught = Assert.Throws<AggregateException>(
			() => {
				_conn.ConnectToPersistentSubscription(
					"nonexisting2",
					"foo",
					(sub, e) => {
						Console.Write("appeared");
						return Task.CompletedTask;
					},
					(sub, reason, ex) => { },
					DefaultData.AdminCredentials);
				throw new Exception("should have thrown");
			}).InnerException;
		return Task.CompletedTask;
	}

	[Test]
	public void the_completion_fails() {
		Assert.IsNotNull(_caught);
	}

	[Test]
	public void the_exception_is_an_argument_exception() {
		Assert.IsInstanceOf<ArgumentException>(_caught);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class connect_to_existing_persistent_subscription_with_permissions<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private EventStorePersistentSubscriptionBase _sub;
	private readonly string _stream = Guid.NewGuid().ToString();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override async Task Given() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, "agroupname17", _settings,
			DefaultData.AdminCredentials);
	}

	protected override Task When() {
		_sub = _conn.ConnectToPersistentSubscription(_stream,
			"agroupname17",
			(sub, e) => {
				Console.Write("appeared");
				return Task.CompletedTask;
			},
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);

		return Task.CompletedTask;
	}

	[Test]
	public void the_subscription_succeeds() {
		Assert.IsNotNull(_sub);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class connect_to_existing_persistent_subscription_without_permissions<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	protected override Task When() {
		return _conn.CreatePersistentSubscriptionAsync(_stream, "agroupname55", _settings,
			DefaultData.AdminCredentials);
	}

	[Test]
	public void the_subscription_fails_to_connect() {
		try {
			_conn.ConnectToPersistentSubscription(
				_stream,
				"agroupname55",
				(sub, e) => {
					Console.Write("appeared");
					return Task.CompletedTask;
				},
				(sub, reason, ex) => Console.WriteLine("dropped."));
			throw new Exception("should have thrown.");
		} catch (Exception ex) {
			Assert.IsInstanceOf<AggregateException>(ex);
			Assert.IsInstanceOf<AccessDeniedException>(ex.InnerException);
		}
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class connect_to_existing_persistent_subscription_with_max_one_client<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent()
		.WithMaxSubscriberCountOf(1);

	private Exception _exception;

	private const string _group = "startinbeginning1";

	protected override async Task Given() {
		await base.Given();
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			(s, e) => {
				s.Acknowledge(e);
				return Task.CompletedTask;
			},
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
	}

	protected override Task When() {
		_exception = Assert.Throws<AggregateException>(() => {
			_conn.ConnectToPersistentSubscription(
				_stream,
				_group,
				(s, e) => {
					s.Acknowledge(e);
					return Task.CompletedTask;
				},
				(sub, reason, ex) => { },
				DefaultData.AdminCredentials);
			throw new Exception("should have thrown.");
		}).InnerException;
		return Task.CompletedTask;
	}

	[Test]
	public void the_second_subscription_fails_to_connect() {
		Assert.IsInstanceOf<MaximumSubscribersReachedException>(_exception);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	connect_to_existing_persistent_subscription_with_start_from_beginning_and_no_stream<TLogFormat, TStreamId> :
		SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromBeginning();

	private readonly Guid _id = Guid.NewGuid();
	private readonly TaskCompletionSource<ResolvedEvent> _firstEventSource = new TaskCompletionSource<ResolvedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

	private const string _group = "startinbeginning1";

	protected override async Task Given() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
	}

	protected override Task When() {
		return _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
			new EventData(_id, "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		_firstEventSource.TrySetResult(resolvedEvent);
		return Task.CompletedTask;
	}

	[Test]
	public async Task the_subscription_gets_event_zero_as_its_first_event() {
		var firstEvent = await _firstEventSource.Task.WithTimeout(TimeSpan.FromSeconds(10));
		Assert.AreEqual(0, firstEvent.Event.EventNumber);
		Assert.AreEqual(_id, firstEvent.Event.EventId);
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	connect_to_existing_persistent_subscription_with_start_from_two_and_no_stream<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFrom(2);

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
	private readonly Guid _id = Guid.NewGuid();
	private readonly TaskCompletionSource<ResolvedEvent> _firstEventSource = new TaskCompletionSource<ResolvedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

	private const string _group = "startinbeginning1";

	protected override async Task Given() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
	}

	protected override async Task When() {
		await _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
				new EventData(Guid.NewGuid(), "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]))
;
		await _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
				new EventData(Guid.NewGuid(), "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]))
;
		await _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
			new EventData(_id, "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		_firstEventSource.TrySetResult(resolvedEvent);
		return Task.CompletedTask;
	}

	[Test]
	public async Task the_subscription_gets_event_two_as_its_first_event() {
		var resolvedEvent = await _firstEventSource.Task.WithTimeout(TimeSpan.FromSeconds(10));
		Assert.AreEqual(2, resolvedEvent.Event.EventNumber);
		Assert.AreEqual(_id, resolvedEvent.Event.EventId);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	connect_to_existing_persistent_subscription_with_start_from_beginning_and_events_in_it<TLogFormat, TStreamId> :
		SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromBeginning();

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
	private ResolvedEvent _firstEvent;
	private List<Guid> _ids = new List<Guid>();
	private bool _set = false;

	private const string _group = "startinbeginning1";

	protected override async Task Given() {
		await WriteEvents(_conn);
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
	}

	private async Task WriteEvents(IEventStoreConnection connection) {
		for (int i = 0; i < 10; i++) {
			_ids.Add(Guid.NewGuid());
			await connection.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
					new EventData(_ids[i], "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]))
;
		}
	}

	protected override Task When() {
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
		return Task.CompletedTask;
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		if (!_set) {
			_set = true;
			_firstEvent = resolvedEvent;
			_resetEvent.Set();
		}

		return Task.CompletedTask;
	}

	[Test]
	public void the_subscription_gets_event_zero_as_its_first_event() {
		Assert.IsTrue(_resetEvent.WaitOne(TimeSpan.FromSeconds(10)));
		Assert.AreEqual(0, _firstEvent.Event.EventNumber);
		Assert.AreEqual(_ids[0], _firstEvent.Event.EventId);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	connect_to_existing_persistent_subscription_with_start_from_beginning_not_set_and_events_in_it<TLogFormat, TStreamId> :
		SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromCurrent();

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);

	private const string _group = "startinbeginning1";

	protected override async Task Given() {
		await WriteEvents(_conn);
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
	}

	private async Task WriteEvents(IEventStoreConnection connection) {
		for (int i = 0; i < 10; i++) {
			await connection.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
					new EventData(Guid.NewGuid(), "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"),
						new byte[0]))
;
		}
	}

	protected override Task When() {
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
		return Task.CompletedTask;
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		_resetEvent.Set();
		return Task.CompletedTask;
	}

	[Test]
	public void the_subscription_gets_no_events() {
		Assert.IsFalse(_resetEvent.WaitOne(TimeSpan.FromSeconds(1)));
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	connect_to_existing_persistent_subscription_with_start_from_beginning_not_set_and_events_in_it_then_event_written<TLogFormat, TStreamId> :
		SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos();

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
	private ResolvedEvent _firstEvent;
	private Guid _id;

	private const string _group = "startinbeginning1";

	protected override async Task Given() {
		await WriteEvents(_conn);
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
	}

	private async Task WriteEvents(IEventStoreConnection connection) {
		for (int i = 0; i < 10; i++) {
			await connection.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
					new EventData(Guid.NewGuid(), "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"),
						new byte[0]))
;
		}
	}

	protected override async Task When() {
		_id = Guid.NewGuid();
		await _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
			new EventData(_id, "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		_firstEvent = resolvedEvent;
		_resetEvent.Set();
		return Task.CompletedTask;
	}

	[Test]
	public void the_subscription_gets_the_written_event_as_its_first_event() {
		Assert.IsTrue(_resetEvent.WaitOne(TimeSpan.FromSeconds(10)));
		Assert.IsNotNull(_firstEvent);
		Assert.AreEqual(10, _firstEvent.Event.EventNumber);
		Assert.AreEqual(_id, _firstEvent.Event.EventId);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	connect_to_existing_persistent_subscription_with_start_from_x_set_higher_than_x_and_events_in_it_then_event_written<TLogFormat, TStreamId> :
		SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFrom(11);

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
	private ResolvedEvent _firstEvent;
	private Guid _id;

	private const string _group = "startinbeginning1";

	protected override async Task Given() {
		await WriteEvents(_conn);
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
	}

	private async Task WriteEvents(IEventStoreConnection connection) {
		for (int i = 0; i < 11; i++) {
			await connection.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
					new EventData(Guid.NewGuid(), "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"),
						new byte[0]))
;
		}
	}

	protected override Task When() {
		_id = Guid.NewGuid();
		return _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
			new EventData(_id, "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		_firstEvent = resolvedEvent;
		_resetEvent.Set();
		return Task.CompletedTask;
	}

	[Test]
	public void the_subscription_gets_the_written_event_as_its_first_event() {
		Assert.IsTrue(_resetEvent.WaitOne(TimeSpan.FromSeconds(10)));
		Assert.IsNotNull(_firstEvent);
		Assert.AreEqual(11, _firstEvent.Event.EventNumber);
		Assert.AreEqual(_id, _firstEvent.Event.EventId);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class a_nak_in_subscription_handler_in_autoack_mode_drops_the_subscription<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromBeginning();

	private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
	private Exception _exception;
	private SubscriptionDropReason _reason;

	private const string _group = "naktest";

	protected override async Task Given() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			Dropped,
			DefaultData.AdminCredentials);
	}

	private void Dropped(EventStorePersistentSubscriptionBase sub, SubscriptionDropReason reason,
		Exception exception) {
		_exception = exception;
		_reason = reason;
		_resetEvent.Set();
	}

	protected override Task When() {
		return _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
				new EventData(Guid.NewGuid(), "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
	}

	private static Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		throw new Exception("test");
	}

	[Test]
	[Retry(5)]
	public void the_subscription_gets_dropped() {
		Assert.IsTrue(_resetEvent.WaitOne(TimeSpan.FromSeconds(5)));
		Assert.AreEqual(SubscriptionDropReason.EventHandlerException, _reason);
		Assert.AreEqual(typeof(Exception), _exception.GetType());
		Assert.AreEqual("test", _exception.Message);
	}
}


[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	connect_to_existing_persistent_subscription_with_start_from_x_set_and_events_in_it_then_event_written<TLogFormat, TStreamId> :
		SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFrom(10);

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
	private ResolvedEvent _firstEvent;
	private Guid _id;

	private const string _group = "startinbeginning1";

	protected override async Task Given() {
		await WriteEvents(_conn);
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
	}

	private async Task WriteEvents(IEventStoreConnection connection) {
		for (int i = 0; i < 10; i++) {
			await connection.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
					new EventData(Guid.NewGuid(), "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"),
						new byte[0]))
;
		}
	}

	protected override Task When() {
		_id = Guid.NewGuid();
		return _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
			new EventData(_id, "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		_firstEvent = resolvedEvent;
		_resetEvent.Set();
		return Task.CompletedTask;
	}

	[Test]
	public void the_subscription_gets_the_written_event_as_its_first_event() {
		Assert.IsTrue(_resetEvent.WaitOne(TimeSpan.FromSeconds(10)));
		Assert.IsNotNull(_firstEvent);
		Assert.AreEqual(10, _firstEvent.Event.EventNumber);
		Assert.AreEqual(_id, _firstEvent.Event.EventId);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	connect_to_existing_persistent_subscription_with_start_from_x_set_and_events_in_it<TLogFormat, TStreamId>
	: SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = "$" + Guid.NewGuid();

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFrom(4);

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
	private ResolvedEvent _firstEvent;
	private Guid _id;

	private const string _group = "startinx2";

	protected override async Task Given() {
		await WriteEvents(_conn);
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		_conn.ConnectToPersistentSubscription(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
	}

	private async Task WriteEvents(IEventStoreConnection connection) {
		for (int i = 0; i < 10; i++) {
			var id = Guid.NewGuid();
			await connection.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
				new EventData(id, "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
			if (i == 4)
				_id = id;
		}
	}

	protected override Task When() {
		return _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
			new EventData(_id, "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
	}

	private bool _set = false;

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		if (_set)
			return Task.CompletedTask;
		_set = true;
		_firstEvent = resolvedEvent;
		_resetEvent.Set();
		return Task.CompletedTask;
	}

	[Test]
	public void the_subscription_gets_the_written_event_as_its_first_event() {
		Assert.IsTrue(_resetEvent.WaitOne(TimeSpan.FromSeconds(10)));
		Assert.IsNotNull(_firstEvent);
		Assert.AreEqual(4, _firstEvent.Event.EventNumber);
		Assert.AreEqual(_id, _firstEvent.Event.EventId);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
[Category("LongRunning")]
public class
	connect_to_persistent_subscription_with_link_to_event_with_event_number_greater_than_int_maxvalue<TLogFormat, TStreamId> :
		ExpectedVersion64Bit.MiniNodeWithExistingRecords<TLogFormat, TStreamId> {
	private const string StreamName =
		"connect_to_persistent_subscription_with_link_to_event_with_event_number_greater_than_int_maxvalue";

	private const long intMaxValue = (long)int.MaxValue;

	private string _linkedStreamName = "linked-" + StreamName;
	private const string _group = "group-" + StreamName;

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.ResolveLinkTos()
		.StartFromBeginning();

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
	private ResolvedEvent _firstEvent;
	private bool _set = false;
	private Guid _event1Id;

	public override async ValueTask WriteTestScenario(CancellationToken token) {
		var event1 = await WriteSingleEvent(StreamName, intMaxValue + 1, new string('.', 3000), token: token);
		await WriteSingleEvent(StreamName, intMaxValue + 2, new string('.', 3000), token: token);
		_event1Id = event1.EventId;
	}

	public override async Task Given() {
		_store = BuildConnection(Node);
		await _store.ConnectAsync();

		await _store.CreatePersistentSubscriptionAsync(_linkedStreamName, _group, _settings,
			DefaultData.AdminCredentials);
		_store.ConnectToPersistentSubscription(
			_linkedStreamName,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials);
		await _store.AppendToStreamAsync(_linkedStreamName, ExpectedVersion.Any, new EventData(Guid.NewGuid(),
			SystemEventTypes.LinkTo, false, Helper.UTF8NoBom.GetBytes(
				string.Format("{0}@{1}", intMaxValue + 1, StreamName)), null));
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent) {
		if (!_set) {
			_set = true;
			_firstEvent = resolvedEvent;
			_resetEvent.Set();
		}

		return Task.CompletedTask;
	}

	[Test]
	public void the_subscription_resolves_the_linked_event_correctly() {
		Assert.IsTrue(_resetEvent.WaitOne(TimeSpan.FromSeconds(10)));
		Assert.AreEqual(intMaxValue + 1, _firstEvent.Event.EventNumber);
		Assert.AreEqual(_event1Id, _firstEvent.Event.EventId);
	}
}

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class connect_to_persistent_subscription_with_retries<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	private readonly string _stream = Guid.NewGuid().ToString("N");

	private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettings.Create()
		.DoNotResolveLinkTos()
		.StartFromBeginning();

	private readonly AutoResetEvent _resetEvent = new AutoResetEvent(false);
	private readonly Guid _id = Guid.NewGuid();
	int? _retryCount;
	private const string _group = "retries";

	protected override async Task Given() {
		await _conn.CreatePersistentSubscriptionAsync(_stream, _group, _settings,
			DefaultData.AdminCredentials);
		await _conn.ConnectToPersistentSubscriptionAsync(
			_stream,
			_group,
			HandleEvent,
			(sub, reason, ex) => { },
			DefaultData.AdminCredentials, autoAck: false);
	}

	protected override Task When() {
		return _conn.AppendToStreamAsync(_stream, ExpectedVersion.Any, DefaultData.AdminCredentials,
			new EventData(_id, "test", true, Encoding.UTF8.GetBytes("{'foo' : 'bar'}"), new byte[0]));
	}

	private Task HandleEvent(EventStorePersistentSubscriptionBase sub, ResolvedEvent resolvedEvent,
		int? retryCount) {
		if (retryCount > 4) {
			_retryCount = retryCount;
			sub.Acknowledge(resolvedEvent);
			_resetEvent.Set();
		} else {
			sub.Fail(resolvedEvent, PersistentSubscriptionNakEventAction.Retry, "Not yet tried enough times");
		}

		return Task.CompletedTask;
	}

	[Test]
	public void events_are_retried_until_success() {
		Assert.IsTrue(_resetEvent.WaitOne(TimeSpan.FromSeconds(10)));
		Assert.AreEqual(5, _retryCount);
	}
}

//ALL

/*

    [TestFixture, Category("LongRunning")]
    public class connect_to_non_existing_persistent_all_subscription_with_permissions : SpecificationWithMiniNode
    {
        private Exception _caught;

        protected override void When()
        {
            try
            {
                _conn.ConnectToPersistentSubscriptionForAll("nonexisting2",
                    (sub, e) => Console.Write("appeared"),
                    (sub, reason, ex) =>
                    {
                    },
                    DefaultData.AdminCredentials);
                throw new Exception("should have thrown");
            }
            catch (Exception ex)
            {
                _caught = ex;
            }
        }

        [Test]
        public void the_completion_fails()
        {
            Assert.IsNotNull(_caught);
        }

        [Test]
        public void the_exception_is_an_argument_exception()
        {
            Assert.IsInstanceOf<ArgumentException>(_caught.InnerException);
        }
    }

    [TestFixture, Category("LongRunning")]
    public class connect_to_existing_persistent_all_subscription_with_permissions : SpecificationWithMiniNode
    {
        private EventStorePersistentSubscription _sub;
        private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettingsBuilder.Create()
                                                                .DoNotResolveLinkTos()
                                                                .StartFromCurrent();
        protected override void When()
        {
            _conn.CreatePersistentSubscriptionForAllAsync("agroupname17", _settings, DefaultData.AdminCredentials).Wait();
            _sub = _conn.ConnectToPersistentSubscriptionForAll("agroupname17",
                (sub, e) => Console.Write("appeared"),
                (sub, reason, ex) => { }, DefaultData.AdminCredentials);
        }

        [Test]
        public void the_subscription_suceeds()
        {
            Assert.IsNotNull(_sub);
        }
    }

    [TestFixture, Category("LongRunning")]
    public class connect_to_existing_persistent_all_subscription_without_permissions : SpecificationWithMiniNode
    {
        private readonly PersistentSubscriptionSettings _settings = PersistentSubscriptionSettingsBuilder.Create()
                                                                .DoNotResolveLinkTos()
                                                                .StartFromCurrent();

        protected override void When()
        {
            _conn.CreatePersistentSubscriptionForAllAsync("agroupname55", _settings,
                DefaultData.AdminCredentials).Wait();
        }

        [Test]
        public void the_subscription_fails_to_connect()
        {
            try
            {
                _conn.ConnectToPersistentSubscriptionForAll("agroupname55",
                    (sub, e) => Console.Write("appeared"),
                    (sub, reason, ex) => { });
                throw new Exception("should have thrown.");
            }
            catch (Exception ex)
            {
                Assert.IsInstanceOf<AggregateException>(ex);
                Assert.IsInstanceOf<AccessDeniedException>(ex.InnerException);
            }
        }
    }
*/
