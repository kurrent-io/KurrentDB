// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Projections.Core.Messages;
using Xunit;
using Xunit.Abstractions;

namespace EventStore.Projections.Core.Javascript.Tests.Integration;

public abstract class SubsystemScenario  : IHandle<Message>, IAsyncLifetime {
	private readonly Func<ValueTask> _stop;
	private readonly SynchronousScheduler _mainBus;
	private readonly IQueuedHandler _mainQueue;

	private readonly InMemoryCheckpoint _writerCheckpoint;
	private readonly MiniStore _miniStore;
	protected CancellationToken TestTimeout { get; }
	private readonly Task _complete;
	private readonly IPublisher _subsystemCommands;
	private readonly Task _ready;
	readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _notifications;

	protected SubsystemScenario(Func<SynchronousScheduler, IQueuedHandler, ICheckpoint, (Func<ValueTask> stopAction, IPublisher subsystemCommands)> createSubsystem, string readyStream, CancellationToken testTimeout) {
		_mainBus = new SynchronousScheduler("main");
		_mainQueue = new QueuedHandlerThreadPool(_mainBus, "bossQ", new QueueStatsManager(), new());
		_writerCheckpoint = new InMemoryCheckpoint(0);
		_miniStore = new MiniStore(_writerCheckpoint, _mainQueue);
		TestTimeout = testTimeout;
		_complete = _miniStore.NotifyAll(TestTimeout);
		_notifications = new ConcurrentDictionary<string, TaskCompletionSource<bool>>();
		_ready = Notify(readyStream);
		_mainBus.Subscribe(this);
		(_stop, _subsystemCommands) = createSubsystem(_mainBus, _mainQueue, _writerCheckpoint);
	}

	protected virtual void OnMainBusMessage(Message msg){}

	public async Task InitializeAsync() {
		_mainQueue.Start();
		_mainQueue.Publish(new SystemMessage.SystemCoreReady());
		_mainQueue.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));
		await _ready.WaitAsync(TestTimeout);
	}

	public async Task DisposeAsync() {
		_miniStore.Complete();
		await _complete;
		await _stop();
		await _mainQueue.Stop();
	}

	protected async Task<(long commitPosition, long nextRevision)> WriteEvents(string stream, long expectedRevision, params Event[] events) {
		var responseEnvelope = new TellMeWhenItsDone(TestTimeout);
		_mainQueue.Publish(new ClientMessage.WriteEvents(Guid.NewGuid(), Guid.NewGuid(),
			responseEnvelope, false, stream, expectedRevision,
			events, null, null));
		var msg = await responseEnvelope.Task.WaitAsync(TestTimeout);
		var resp = Assert.IsType<ClientMessage.WriteEventsCompleted>(msg);
		Assert.Equal(OperationResult.Success, resp.Result);
		return (resp.CommitPosition, resp.CurrentVersion);
	}
	protected async Task<IReadOnlyList<ResolvedEvent>> ReadStream(string stream, int from) {
		var tmwid = new TellMeWhenItsDone(TestTimeout);
		_mainQueue.Publish(new ClientMessage.ReadStreamEventsForward(Guid.NewGuid(),
			Guid.NewGuid(), tmwid, stream, from, 100, true, false, null,
			ClaimsPrincipal.Current, replyOnExpired: false, null, null));
		var msg = await tmwid.Task.WaitAsync(TestTimeout);
		var rr = Assert.IsType<ClientMessage.ReadStreamEventsForwardCompleted>(msg);
		return rr.Events;
	}

	protected async Task<TResponse> SendProjectionMessage<TResponse>(Func<IEnvelope,
		ProjectionManagementMessage.Command.ControlMessage> requestFactory)
		where TResponse : Message {
		var tmwid = new TellMeWhenItsDone(TestTimeout);
		_subsystemCommands.Publish(requestFactory(tmwid));
		var msg = await tmwid.Task.WaitAsync(TestTimeout);
		return Assert.IsType<TResponse>(msg);
	}
	class TellMeWhenItsDone : IEnvelope {
		private readonly TaskCompletionSource<Message> _completion;
		private readonly CancellationTokenRegistration _registration;

		public TellMeWhenItsDone(CancellationToken token) {
			_completion = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
			_registration = token.Register(static o => {
				if (o is not TellMeWhenItsDone state) return;
				state._registration.Dispose();
				state._completion.TrySetCanceled(state._registration.Token);
			}, this);
		}

		public Task<Message> Task => _completion.Task;

		public void ReplyWith<T>(T message) where T : Message {
			_completion.TrySetResult(message);
		}
	}

	protected Task Notify(string streamName) {
		return _notifications.GetOrAdd(streamName, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
	}

	public void Handle(Message message) {
		switch (message) {
			case SystemMessage.SystemCoreReady:
			case SystemMessage.BecomeLeader:
			case ProjectionCoreServiceMessage.SubComponentStarted:
			case AwakeServiceMessage.SubscribeAwake:
			case SystemMessage.SubSystemInitialized:
				return;
			case ClientMessage.ReadStreamEventsForward m :
				_miniStore.Handle(m);
				return;
			case ClientMessage.ReadStreamEventsBackward m :
				_miniStore.Handle(m);
				return;
			case ClientMessage.ReadAllEventsForward m :
				_miniStore.Handle(m);
				return;
			case ClientMessage.WriteEvents m :
				_miniStore.Handle(m);
				return;
			case StorageMessage.EventCommitted e:
				CheckNotifications(e);
				break;
		}
		OnMainBusMessage(message);
	}

	private void CheckNotifications(StorageMessage.EventCommitted e) {
		if (_notifications.TryRemove(e.Event.EventStreamId, out var tcs)) {
			tcs.TrySetResult(true);
		}
	}

	protected ResolvedEvent AtLogPosition(long position) {
		return _miniStore.AtPosition(position);
	}

	//A super mini in memory event store purely for testing the projection runtime
	class MiniStore :
		IHandle<ClientMessage.ReadStreamEventsForward>,
		IHandle<ClientMessage.ReadAllEventsForward>,
		IHandle<ClientMessage.ReadStreamEventsBackward>,
	IHandle<ClientMessage.WriteEvents> {
		private readonly ICheckpoint _checkpoint;
		private readonly IPublisher _bus;
		private readonly List<ResolvedEvent> _all;
		private readonly Dictionary<string, List<ResolvedEvent>> _streams;
		private readonly Channel<int> _notifications;

		public MiniStore(ICheckpoint checkpoint, IPublisher bus) {
			_checkpoint = checkpoint;
			_bus = bus;
			_all = new List<ResolvedEvent>();
			_streams = new Dictionary<string, List<ResolvedEvent>>();
			_notifications = Channel.CreateUnbounded<int>(new UnboundedChannelOptions(){AllowSynchronousContinuations = false, SingleReader = true, SingleWriter = true});
		}

		public async Task NotifyAll(CancellationToken cancellationToken) {
			try {
				int next = 0;
				await foreach (var upTo in
				               _notifications.Reader.ReadAllAsync(cancellationToken)) {
					while (next < upTo) {
						var nextEvent = _all[next].OriginalEvent;
						_bus.Publish(new StorageMessage.EventCommitted(next,nextEvent, upTo-1==next));
						next++;
					}

					if (cancellationToken.IsCancellationRequested) break;
				}
			}catch(Exception ex) when (ex is TaskCanceledException or OperationCanceledException){}
		}
		public void Handle(ClientMessage.ReadStreamEventsForward message) {
			ClientMessage.ReadStreamEventsForwardCompleted response;
			if (_streams.TryGetValue(message.EventStreamId, out var events)) {
				var from = (int)message.FromEventNumber;
				response = new ClientMessage.ReadStreamEventsForwardCompleted(
					message.CorrelationId, message.EventStreamId, message.FromEventNumber,
					message.MaxCount, ReadStreamResult.Success,
					events.Skip(from).Take(message.MaxCount).ToArray(), StreamMetadata.Empty,
					false, null, Math.Min(from + message.MaxCount + 1, events.Count),
					events.Count - 1, from + message.MaxCount >= events.Count,
					_checkpoint.Read());

			} else {
				response = new ClientMessage.ReadStreamEventsForwardCompleted(
					message.CorrelationId, message.EventStreamId, message.FromEventNumber,
					message.MaxCount, ReadStreamResult.NoStream, Array.Empty<ResolvedEvent>(),
					StreamMetadata.Empty, false, null, -1, -1, true, _checkpoint.Read());
			}
			message.Envelope.ReplyWith(response);
		}

		public void Handle(ClientMessage.ReadAllEventsForward message) {
			ClientMessage.ReadAllEventsForwardCompleted response;
			if (message.CommitPosition < _all.Count) {
				var resolvedEvents = _all.Skip((int)message.CommitPosition).Take(message.MaxCount).ToArray();
				var next = (int)Math.Min(_all.Count, message.CommitPosition + message.MaxCount);
				var prev = Math.Max(0, message.CommitPosition - 1);
				response = new ClientMessage.ReadAllEventsForwardCompleted(
					message.CorrelationId, ReadAllResult.Success, null,
					resolvedEvents, StreamMetadata.Empty, false, message.MaxCount,
					new TFPos(message.CommitPosition, message.PreparePosition), new TFPos(next, next), new TFPos(prev, prev > 0 ? prev : long.MaxValue), _checkpoint.Read());
			} else {
				response = new ClientMessage.ReadAllEventsForwardCompleted(
					message.CorrelationId, ReadAllResult.Success, null,
					Array.Empty<ResolvedEvent>(), StreamMetadata.Empty, false, message.MaxCount,
					new TFPos(message.CommitPosition, message.PreparePosition), new TFPos(message.CommitPosition, message.PreparePosition), new TFPos(0, long.MaxValue), _checkpoint.Read());
			}
			message.Envelope.ReplyWith(response);

		}

		public void Handle(ClientMessage.ReadStreamEventsBackward message) {
			ClientMessage.ReadStreamEventsBackwardCompleted response;
			if (_streams.TryGetValue(message.EventStreamId, out var events)) {
				var from = (int)message.FromEventNumber;
				response = new ClientMessage.ReadStreamEventsBackwardCompleted(
					message.CorrelationId, message.EventStreamId, message.FromEventNumber,
					message.MaxCount, ReadStreamResult.Success,
					events.AsEnumerable().Reverse().Skip(from).Take(message.MaxCount).ToArray(), StreamMetadata.Empty,
					false, null, Math.Max(from - message.MaxCount, 0),
					events.Count - 1, from + message.MaxCount >= events.Count,
					_checkpoint.Read());

			} else {
				response = new ClientMessage.ReadStreamEventsBackwardCompleted(
					message.CorrelationId, message.EventStreamId, message.FromEventNumber,
					message.MaxCount, ReadStreamResult.NoStream, Array.Empty<ResolvedEvent>(),
					StreamMetadata.Empty, false, null, -1, -1, true, _checkpoint.Read());
			}
			message.Envelope.ReplyWith(response);
		}

		public void Handle(ClientMessage.WriteEvents message) {
			ClientMessage.WriteEventsCompleted response;
			if (_streams.TryGetValue(message.EventStreamId, out var events)) {
				if (message.ExpectedVersion == ExpectedVersion.Any ||
				    message.ExpectedVersion == events.Count - 1) {
					response = WriteEvents(message, events);
				} else {
					response = new ClientMessage.WriteEventsCompleted(message.CorrelationId,
						OperationResult.WrongExpectedVersion, "Wrong expected version",
						_streams.Count - 1);
				}
			} else {
				if (message.ExpectedVersion is ExpectedVersion.Any or ExpectedVersion.NoStream) {
					events = new List<ResolvedEvent>();
					_streams.Add(message.EventStreamId, events);
					response = WriteEvents(message, events);
				} else {
					response = new ClientMessage.WriteEventsCompleted(message.CorrelationId,
						OperationResult.WrongExpectedVersion, "Wrong expected version",
						ExpectedVersion.NoStream);
				}
			}

			message.Envelope.ReplyWith(response);

			_notifications.Writer.TryWrite((int)_checkpoint.Read());
		}
		ClientMessage.WriteEventsCompleted WriteEvents(ClientMessage.WriteEvents message, List<ResolvedEvent> events) {
			var stored = new List<ResolvedEvent>();
			for (int i = 0; i < message.Events.Length; i++) {
				var position = _all.Count + i;
				var revision = events.Count + i;
				var current = message.Events[i];

				var flags = PrepareFlags.IsCommitted | PrepareFlags.Data;
				if (current.IsJson) flags |= PrepareFlags.IsJson;
				if (i == 0) flags |= PrepareFlags.TransactionBegin;
				if(i==message.Events.Length-1) flags |= PrepareFlags.TransactionEnd;

				var record = new EventRecord(revision, position, message.CorrelationId,
					current.EventId, _all.Count, i, message.EventStreamId, -1, DateTime.Now,
					flags, current.EventType, current.Data, current.Metadata);
				if (current.EventType == SystemEventTypes.LinkTo) {
					var data = Encoding.UTF8.GetString(current.Data);
					var parts = data.Split('@', 2);
					var number = int.Parse(parts[0]);
					var stream = parts[1];
					if (_streams.TryGetValue(stream, out var links) &&
					    links.Count > number) {
						stored.Add(ResolvedEvent.ForResolvedLink(links[number].OriginalEvent, record, position));
					} else {
						stored.Add(ResolvedEvent.ForFailedResolvedLink(record, ReadEventResult.NotFound));
					}
				} else {
					stored.Add(ResolvedEvent.ForUnresolvedEvent(record, position));
				}

				if (stored.Count > 1) {
					if (stored[i].OriginalPosition < stored[i - 1].OriginalPosition) {
						Debug.WriteLine("500");
					}
				}
			}

			var response = new ClientMessage.WriteEventsCompleted(message.CorrelationId,
				events.Count, Math.Max(0, events.Count + stored.Count - 1),
				_all.Count + stored.Count, _all.Count + stored.Count);
			events.AddRange(stored);
			_all.AddRange(stored);
			_checkpoint.Write(_all.Count);
			_checkpoint.Flush();
			return response;

		}

		public void Complete() {
			_notifications.Writer.TryComplete();
		}

		public ResolvedEvent AtPosition(long position) {
			var local = (int)position;
			if (local < 0 || local >= _all.Count) return default;
			return _all[local];
		}
	}
}

