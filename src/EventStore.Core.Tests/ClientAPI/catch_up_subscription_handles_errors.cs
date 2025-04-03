// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.ClientOperations;
using EventStore.ClientAPI.Common.Log;
using EventStore.ClientAPI.Internal;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;
using ClientMessage = EventStore.ClientAPI.Messages.ClientMessage;
using ResolvedEvent = EventStore.ClientAPI.ResolvedEvent;
using StreamMetadata = EventStore.ClientAPI.StreamMetadata;
using SystemSettings = EventStore.ClientAPI.SystemSettings;

namespace EventStore.Core.Tests.ClientAPI;

[TestFixture, Category("ClientAPI"), Category("LongRunning")]
public class catch_up_subscription_handles_errors {
	private static readonly int TimeoutMs = Debugger.IsAttached ? Timeout.Infinite : 2000;
	private FakeEventStoreConnection _connection;
	private IList<ResolvedEvent> _raisedEvents;
	private bool _liveProcessingStarted;
	private bool _isDropped;
	private TaskCompletionSource<bool> _dropEvent;
	private TaskCompletionSource<bool> _raisedEventEvent;
	private Exception _dropException;
	private SubscriptionDropReason _dropReason;
	private EventStoreStreamCatchUpSubscription _subscription;
	private static readonly string StreamId = "stream1";

	[SetUp]
	public void SetUp() {
		_connection = new FakeEventStoreConnection();
		_raisedEvents = new List<ResolvedEvent>();
		_dropEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_raisedEventEvent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_liveProcessingStarted = false;
		_isDropped = false;
		_dropReason = SubscriptionDropReason.Unknown;
		_dropException = null;

		var settings = new CatchUpSubscriptionSettings(1, 1, false, false, String.Empty);
		_subscription = new EventStoreStreamCatchUpSubscription(_connection, new NoopLogger(), StreamId, null, null,
			(subscription, ev) => {
				_raisedEvents.Add(ev);
				if (_raisedEvents.Count >= 2) {
					_raisedEventEvent.TrySetResult(true);
				}
				return Task.CompletedTask;
			},
			subscription => { _liveProcessingStarted = true; },
			(subscription, reason, ex) => {
				_isDropped = true;
				_dropReason = reason;
				_dropException = ex;
				_dropEvent.TrySetResult(true);
			},
			settings);
	}

	[Test]
	public async Task read_events_til_stops_subscription_when_throws_immediately() {
		var expectedException = new ApplicationException("Test");

		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => {
			Assert.That(stream, Is.EqualTo(StreamId));
			Assert.That(start, Is.EqualTo(0));
			Assert.That(max, Is.EqualTo(1));
			throw expectedException;
		});

		await AssertStartFailsAndDropsSubscriptionWithException(expectedException);
	}

	private async Task AssertStartFailsAndDropsSubscriptionWithException(ApplicationException expectedException) {
		await AssertEx.ThrowsAsync<ApplicationException>(() => _subscription.StartAsync());
		Assert.That(_isDropped);
		Assert.That(_dropReason, Is.EqualTo(SubscriptionDropReason.CatchUpError));
		Assert.That(_dropException, Is.SameAs(expectedException));
		Assert.That(_liveProcessingStarted, Is.False);
	}

	[Test]
	public async Task read_events_til_stops_subscription_when_throws_asynchronously() {
		var expectedException = new ApplicationException("Test");
		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => {
			var taskCompletionSource =
				new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
			Assert.That(stream, Is.EqualTo(StreamId));
			Assert.That(start, Is.EqualTo(0));
			Assert.That(max, Is.EqualTo(1));
			taskCompletionSource.SetException(expectedException);
			return taskCompletionSource.Task;
		});

		await AssertStartFailsAndDropsSubscriptionWithException(expectedException);
	}

	[Test]
	public async Task read_events_til_stops_subscription_when_second_read_throws_immediately() {
		var expectedException = new ApplicationException("Test");

		int callCount = 0;

		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => {
			Assert.That(stream, Is.EqualTo(StreamId));
			Assert.That(max, Is.EqualTo(1));

			if (callCount++ == 0) {
				Assert.That(start, Is.EqualTo(0));
				var taskCompletionSource =
					new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
				taskCompletionSource.SetResult(CreateStreamEventsSlice());
				return taskCompletionSource.Task;
			} else {
				Assert.That(start, Is.EqualTo(1));
				throw expectedException;
			}
		});

		await AssertStartFailsAndDropsSubscriptionWithException(expectedException);
		Assert.That(_raisedEvents.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task read_events_til_stops_subscription_when_second_read_throws_asynchronously() {
		var expectedException = new ApplicationException("Test");

		int callCount = 0;

		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => {
			Assert.That(stream, Is.EqualTo(StreamId));
			Assert.That(max, Is.EqualTo(1));

			if (callCount++ == 0) {
				Assert.That(start, Is.EqualTo(0));
				var taskCompletionSource =
					new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
				taskCompletionSource.SetResult(CreateStreamEventsSlice());
				return taskCompletionSource.Task;
			} else {
				Assert.That(start, Is.EqualTo(1));
				var taskCompletionSource =
					new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
				taskCompletionSource.SetException(expectedException);
				return taskCompletionSource.Task;
			}
		});

		await AssertStartFailsAndDropsSubscriptionWithException(expectedException);
		Assert.That(_raisedEvents.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task start_stops_subscription_if_subscribe_fails_immediately() {
		var expectedException = new ApplicationException("Test");

		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => {
			var taskCompletionSource =
				new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
			taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
			return taskCompletionSource.Task;
		});

		_connection.HandleSubscribeToStreamAsync((stream, raise, drop) => {
			Assert.That(stream, Is.EqualTo(StreamId));
			throw expectedException;
		});

		await AssertStartFailsAndDropsSubscriptionWithException(expectedException);
		Assert.That(_raisedEvents.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task start_stops_subscription_if_subscribe_fails_async() {
		var expectedException = new ApplicationException("Test");

		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => {
			var taskCompletionSource =
				new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
			taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
			return taskCompletionSource.Task;
		});

		_connection.HandleSubscribeToStreamAsync((stream, raise, drop) => {
			var taskCompletionSource =
				new TaskCompletionSource<EventStoreSubscription>(TaskCreationOptions
					.RunContinuationsAsynchronously);
			taskCompletionSource.SetException(expectedException);
			return taskCompletionSource.Task;
		});

		await AssertStartFailsAndDropsSubscriptionWithException(expectedException);
		Assert.That(_raisedEvents.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task start_stops_subscription_if_historical_missed_events_load_fails_immediate() {
		var expectedException = new ApplicationException("Test");

		int callCount = 0;
		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => {
			if (callCount++ == 0) {
				var taskCompletionSource =
					new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
				taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
				return taskCompletionSource.Task;
			} else {
				Assert.That(start, Is.EqualTo(1));
				throw expectedException;
			}
		});

		_connection.HandleSubscribeToStreamAsync((stream, raise, drop) => {
			var taskCompletionSource =
				new TaskCompletionSource<EventStoreSubscription>(TaskCreationOptions
					.RunContinuationsAsynchronously);
			taskCompletionSource.SetResult(CreateVolatileSubscription(raise, drop, 1));
			return taskCompletionSource.Task;
		});

		await AssertStartFailsAndDropsSubscriptionWithException(expectedException);
		Assert.That(_raisedEvents.Count, Is.EqualTo(1));
	}


	[Test]
	public async Task start_stops_subscription_if_historical_missed_events_load_fails_async() {
		var expectedException = new ApplicationException("Test");

		int callCount = 0;
		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => {
			if (callCount++ == 0) {
				var taskCompletionSource =
					new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
				taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
				return taskCompletionSource.Task;
			} else {
				Assert.That(start, Is.EqualTo(1));
				var taskCompletionSource =
					new TaskCompletionSource<StreamEventsSlice>(TaskCreationOptions.RunContinuationsAsynchronously);
				taskCompletionSource.SetException(expectedException);
				return taskCompletionSource.Task;
			}
		});

		_connection.HandleSubscribeToStreamAsync((stream, raise, drop) => {
			var taskCompletionSource =
				new TaskCompletionSource<EventStoreSubscription>(TaskCreationOptions
					.RunContinuationsAsynchronously);
			taskCompletionSource.SetResult(CreateVolatileSubscription(raise, drop, 1));
			return taskCompletionSource.Task;
		});

		await AssertStartFailsAndDropsSubscriptionWithException(expectedException);
		Assert.That(_raisedEvents.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task start_completes_onces_subscription_is_live() {
		var finalEvent = new ManualResetEventSlim();
		int callCount = 0;
		_connection.HandleReadStreamEventsForwardAsync((stream, start, max) => Task.Run(() => {
			callCount++;

			if (callCount == 1) {
				return Task.FromResult(CreateStreamEventsSlice(isEnd: true));
			} else if (callCount == 2) {
				var result = Task.FromResult(CreateStreamEventsSlice(fromEvent: 1, isEnd: true));
				Assert.That(finalEvent.Wait(TimeoutMs));
				return result;
			} else {
				throw new InvalidOperationException("Should not get a third call");
			}
		}));

		_connection.HandleSubscribeToStreamAsync((stream, raise, drop) =>
			Task.FromResult<EventStoreSubscription>(CreateVolatileSubscription(raise, drop, 1)));

		var task = _subscription.StartAsync();

		Assert.That(task.Status, Is.Not.EqualTo(TaskStatus.RanToCompletion));

		finalEvent.Set();

		await task.WithTimeout(TimeoutMs);
	}

	private static VolatileEventStoreSubscription CreateVolatileSubscription(
		Func<EventStoreSubscription, ResolvedEvent, Task> raise,
		Action<EventStoreSubscription, SubscriptionDropReason, Exception> drop, int? lastEventNumber) {
		return new VolatileEventStoreSubscription(
			new VolatileSubscriptionOperation(new NoopLogger(),
				new TaskCompletionSource<EventStoreSubscription>(TaskCreationOptions
					.RunContinuationsAsynchronously), StreamId, false, null, raise, drop, false, () => null),
			StreamId, -1, lastEventNumber);
	}

	private static StreamEventsSlice CreateStreamEventsSlice(int fromEvent = 0, int count = 1, bool isEnd = false) {
		var events = Enumerable.Range(0, count)
			.Select(
				i =>
					new ClientMessage.ResolvedIndexedEvent(
						new ClientMessage.EventRecord(StreamId, i, Guid.NewGuid().ToByteArray(), null, 0, 0, null,
							null, null, null), null))
			.ToArray();

		return new StreamEventsSlice(SliceReadStatus.Success, StreamId, fromEvent, ReadDirection.Forward, events,
			fromEvent + count, 100, isEnd);
	}
}

internal class FakeEventStoreConnection : IEventStoreConnection {
	private Func<Position, int, bool, UserCredentials, Task<AllEventsSlice>> _readAllEventsForwardAsync;
	private Func<string, long, int, Task<StreamEventsSlice>> _readStreamEventsForwardAsync;

	private Func<string, Func<EventStoreSubscription, ResolvedEvent, Task>,
			Action<EventStoreSubscription, SubscriptionDropReason, Exception>, Task<EventStoreSubscription>>
		_subscribeToStreamAsync;

	private Func<bool, Func<EventStoreSubscription, ResolvedEvent, Task>,
			Action<EventStoreSubscription, SubscriptionDropReason, Exception>, Task<EventStoreSubscription>>
		_subscribeToAllAsync;

	public void Dispose() {
		throw new NotImplementedException();
	}

	public string ConnectionName { get; }

	public ConnectionSettings Settings {
		get { return null; }
	}

	public Task ConnectAsync() {
		throw new NotImplementedException();
	}

	public void Close() {
		throw new NotImplementedException();
	}

	public Task<DeleteResult> DeleteStreamAsync(string stream, long expectedVersion,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<DeleteResult> DeleteStreamAsync(string stream, long expectedVersion, bool hardDelete,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<WriteResult> AppendToStreamAsync(string stream, long expectedVersion, params EventData[] events) {
		throw new NotImplementedException();
	}

	public Task<WriteResult> AppendToStreamAsync(string stream, long expectedVersion,
		UserCredentials userCredentials, params EventData[] events) {
		throw new NotImplementedException();
	}

	public Task<WriteResult> AppendToStreamAsync(string stream, long expectedVersion, IEnumerable<EventData> events,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<ConditionalWriteResult> ConditionalAppendToStreamAsync(string stream, long expectedVersion,
		IEnumerable<EventData> events,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<EventStoreTransaction> StartTransactionAsync(string stream, long expectedVersion,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public EventStoreTransaction ContinueTransaction(long transactionId, UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<EventReadResult> ReadEventAsync(string stream, long eventNumber, bool resolveLinkTos,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public void HandleReadStreamEventsForwardAsync(Func<string, long, int, Task<StreamEventsSlice>> callback) {
		_readStreamEventsForwardAsync = callback;
	}

	public Task<StreamEventsSlice> ReadStreamEventsForwardAsync(string stream, long start, int count,
		bool resolveLinkTos,
		UserCredentials userCredentials = null) {
		return _readStreamEventsForwardAsync(stream, start, count);
	}

	public Task<StreamEventsSlice> ReadStreamEventsBackwardAsync(string stream, long start, int count,
		bool resolveLinkTos,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public void HandleReadAllEventsForwardAsync(
		Func<Position, int, bool, UserCredentials, Task<AllEventsSlice>> callback) {
		_readAllEventsForwardAsync = callback;
	}

	public Task<AllEventsSlice> ReadAllEventsForwardAsync(Position position, int maxCount, bool resolveLinkTos,
		UserCredentials userCredentials = null) {
		return _readAllEventsForwardAsync(position, maxCount, resolveLinkTos, userCredentials);
	}

	public Task<AllEventsSlice> FilteredReadAllEventsForwardAsync(Position position, int maxCount, bool resolveLinkTos, Filter filter,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<AllEventsSlice> FilteredReadAllEventsForwardAsync(Position position, int maxCount, bool resolveLinkTos, Filter filter,
		int maxSearchWindow, UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<AllEventsSlice> ReadAllEventsBackwardAsync(Position position, int maxCount, bool resolveLinkTos,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<AllEventsSlice> FilteredReadAllEventsBackwardAsync(Position position, int maxCount, bool resolveLinkTos, Filter filter,
		int maxSearchWindow, UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<AllEventsSlice> FilteredReadAllEventsBackwardAsync(Position position, int maxCount, bool resolveLinkTos, Filter filter,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public void HandleSubscribeToStreamAsync(
		Func<string, Func<EventStoreSubscription, ResolvedEvent, Task>,
				Action<EventStoreSubscription, SubscriptionDropReason, Exception>, Task<EventStoreSubscription>>
			callback) {
		_subscribeToStreamAsync = callback;
	}

	public Task<EventStoreSubscription> SubscribeToStreamAsync(string stream, bool resolveLinkTos,
		Func<EventStoreSubscription, ResolvedEvent, Task> eventAppeared,
		Action<EventStoreSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null) {
		return _subscribeToStreamAsync(stream, eventAppeared, subscriptionDropped);
	}

	public EventStoreStreamCatchUpSubscription SubscribeToStreamFrom(string stream, long? lastCheckpoint,
		bool resolveLinkTos,
		Func<EventStoreCatchUpSubscription, ResolvedEvent, Task> eventAppeared,
		Action<EventStoreCatchUpSubscription> liveProcessingStarted = null,
		Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null, int readBatchSize = 500, string subscriptionName = "") {
		throw new NotImplementedException();
	}

	public EventStoreStreamCatchUpSubscription SubscribeToStreamFrom(
		string stream,
		long? lastCheckpoint,
		CatchUpSubscriptionSettings settings,
		Func<EventStoreCatchUpSubscription, ResolvedEvent, Task> eventAppeared,
		Action<EventStoreCatchUpSubscription> liveProcessingStarted = null,
		Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public void HandleSubscribeToAllAsync(
		Func<bool, Func<EventStoreSubscription, ResolvedEvent, Task>,
				Action<EventStoreSubscription, SubscriptionDropReason, Exception>, Task<EventStoreSubscription>>
			callback) {
		_subscribeToAllAsync = callback;
	}

	public Task<EventStoreSubscription> SubscribeToAllAsync(bool resolveLinkTos,
		Func<EventStoreSubscription, ResolvedEvent, Task> eventAppeared,
		Action<EventStoreSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null) {
		return _subscribeToAllAsync(resolveLinkTos, eventAppeared, subscriptionDropped);
	}

	public Task<EventStoreSubscription> FilteredSubscribeToAllAsync(bool resolveLinkTos, Filter filter, Func<EventStoreSubscription, ResolvedEvent, Task> eventAppeared, Func<EventStoreSubscription, Position, Task> checkpointReached,
		int checkpointInterval, Action<EventStoreSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null, UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<EventStoreSubscription> FilteredSubscribeToAllAsync(bool resolveLinkTos, Filter filter, Func<EventStoreSubscription, ResolvedEvent, Task> eventAppeared,
		Action<EventStoreSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null, UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<EventStoreSubscription> SubscribeToAllFilteredAsync(bool resolveLinkTos, Filter filter,
		Func<EventStoreSubscription, ResolvedEvent, Task> eventAppeared,
		Func<EventStoreSubscription, Position, Task> checkpointReached = null,
		int? checkpointInterval = null,
		Action<EventStoreSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public EventStorePersistentSubscriptionBase ConnectToPersistentSubscription(string stream, string groupName,
		Func<EventStorePersistentSubscriptionBase, ResolvedEvent, int?, Task> eventAppeared,
		Action<EventStorePersistentSubscriptionBase, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null, int bufferSize = 10,
		bool autoAck = true) {
		throw new NotImplementedException();
	}

	public Task<EventStorePersistentSubscriptionBase> ConnectToPersistentSubscriptionAsync(string stream,
		string groupName, Func<EventStorePersistentSubscriptionBase, ResolvedEvent, int?, Task> eventAppeared,
		Action<EventStorePersistentSubscriptionBase, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null, int bufferSize = 10,
		bool autoAck = true) {
		throw new NotImplementedException();
	}

	public EventStoreAllCatchUpSubscription SubscribeToAllFrom(Position? lastCheckpoint, bool resolveLinkTos,
		Func<EventStoreCatchUpSubscription, ResolvedEvent, Task> eventAppeared,
		Action<EventStoreCatchUpSubscription> liveProcessingStarted = null,
		Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null,
		int readBatchSize = 500,
		string subscriptionName = "") {
		throw new NotImplementedException();
	}

	public EventStoreAllCatchUpSubscription SubscribeToAllFrom(
		Position? lastCheckpoint,
		CatchUpSubscriptionSettings settings,
		Func<EventStoreCatchUpSubscription, ResolvedEvent, Task> eventAppeared,
		Action<EventStoreCatchUpSubscription> liveProcessingStarted = null,
		Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public EventStoreAllFilteredCatchUpSubscription FilteredSubscribeToAllFrom(Position? lastCheckpoint, Filter filter,
		CatchUpSubscriptionFilteredSettings settings, Func<EventStoreCatchUpSubscription, ResolvedEvent, Task> eventAppeared, Func<EventStoreCatchUpSubscription, Position, Task> checkpointReached,
		int checkpointIntervalMultiplier, Action<EventStoreCatchUpSubscription> liveProcessingStarted = null, Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public EventStoreAllFilteredCatchUpSubscription FilteredSubscribeToAllFrom(Position? lastCheckpoint, Filter filter,
		CatchUpSubscriptionFilteredSettings settings, Func<EventStoreCatchUpSubscription, ResolvedEvent, Task> eventAppeared, Action<EventStoreCatchUpSubscription> liveProcessingStarted = null,
		Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null, UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task UpdatePersistentSubscriptionAsync(string stream, string groupName,
		PersistentSubscriptionSettings settings,
		UserCredentials credentials) {
		throw new NotImplementedException();
	}

	public Task CreatePersistentSubscriptionAsync(string stream, string groupName,
		PersistentSubscriptionSettings settings,
		UserCredentials credentials) {
		throw new NotImplementedException();
	}

	public Task DeletePersistentSubscriptionAsync(string stream, string groupName,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<WriteResult> SetStreamMetadataAsync(string stream, long expectedMetastreamVersion,
		StreamMetadata metadata,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<WriteResult> SetStreamMetadataAsync(string stream, long expectedMetastreamVersion, byte[] metadata,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<StreamMetadataResult>
		GetStreamMetadataAsync(string stream, UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task<RawStreamMetadataResult> GetStreamMetadataAsRawBytesAsync(string stream,
		UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public Task SetSystemSettingsAsync(SystemSettings settings, UserCredentials userCredentials = null) {
		throw new NotImplementedException();
	}

	public event EventHandler<ClientConnectionEventArgs> Connected;
	public event EventHandler<ClientConnectionEventArgs> Disconnected;
	public event EventHandler<ClientReconnectingEventArgs> Reconnecting;
	public event EventHandler<ClientClosedEventArgs> Closed;
	public event EventHandler<ClientErrorEventArgs> ErrorOccurred;
	public event EventHandler<ClientAuthenticationFailedEventArgs> AuthenticationFailed;

	protected virtual void OnErrorOccurred(ClientErrorEventArgs e) {
		var handler = ErrorOccurred;
		if (handler != null)
			handler(this, e);
	}

	protected virtual void OnAuthenticationFailed(ClientAuthenticationFailedEventArgs e) {
		var handler = AuthenticationFailed;
		if (handler != null)
			handler(this, e);
	}

	protected virtual void OnClosed(ClientClosedEventArgs e) {
		var handler = Closed;
		if (handler != null)
			handler(this, e);
	}

	protected virtual void OnReconnecting(ClientReconnectingEventArgs e) {
		var handler = Reconnecting;
		if (handler != null)
			handler(this, e);
	}

	protected virtual void OnDisconnected(ClientConnectionEventArgs e) {
		var handler = Disconnected;
		if (handler != null)
			handler(this, e);
	}

	public void OnConnected(ClientConnectionEventArgs e) {
		var handler = Connected;
		if (handler != null)
			handler(this, e);
	}
}
