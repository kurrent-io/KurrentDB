// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.TimerService;
using AwakeServiceMessage = KurrentDB.Core.Services.AwakeReaderService.AwakeServiceMessage;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.Core.Helpers;


public sealed class IODispatcher : IHandle<IODispatcherDelayedMessage>, IHandle<ClientMessage.NotHandled> {
	public sealed class RequestTracking {
		public RequestTracking(bool trackPendingRequests) {
			_trackPendingRequests = trackPendingRequests;
		}

		private readonly WriterQueueSet _writerQueueSet = new WriterQueueSet();
		private readonly PendingWrites _pendingWrites = new PendingWrites();
		private readonly PendingReads _pendingReads = new PendingReads();
		private readonly bool _trackPendingRequests;
		private readonly HashSet<Guid> _allPendingRequests = new HashSet<Guid>();
		private bool _draining;
		private Action _onRequestsDrained;
		private readonly object _lockObject = new object();

		public void StartDraining(Action onRequestsDrained) {
			lock (_lockObject) {
				if (_allPendingRequests.IsEmpty()) {
					onRequestsDrained?.Invoke();
					return;
				}
				_draining = true;
				_onRequestsDrained = onRequestsDrained;
			}
		}

		public void AddPendingRequest(Guid correlationId) {
			lock (_lockObject) {
				if (!_trackPendingRequests)
					return;

				_allPendingRequests.Add(correlationId);
			}
		}

		public void RemovePendingRequest(Guid correlationId) {
			lock (_lockObject) {
				if (!_trackPendingRequests)
					return;

				_allPendingRequests.Remove(correlationId);
				if (_draining && _allPendingRequests.IsEmpty()) {
					_onRequestsDrained?.Invoke();
					_onRequestsDrained = null;
					_draining = false;
				}
			}
		}

		public void AddPendingRead(Guid corrId) {
			lock (_lockObject) {
				AddPendingRequest(corrId);
				_pendingReads.Register(corrId);
			}
		}

		public bool RemovePendingRead(Guid corrId) {
			lock (_lockObject) {
				RemovePendingRequest(corrId);
				if (!_pendingReads.IsRegistered(corrId))
					return false;
				_pendingReads.Remove(corrId);
				return true;
			}
		}

		private void WorkQueue(
			Guid key,
			RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted> writer) {

			if (_writerQueueSet.IsBusy(key))
				return;
			if (!_writerQueueSet.HasPendingWrites(key))
				return;
			var write = _writerQueueSet.Dequeue(key);
			if (write != null) {
				writer.Publish(write, (msg) => Handle(key, msg, writer));
			}
		}

		private void Handle(
			Guid key,
			ClientMessage.WriteEventsCompleted message,
			RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted> writer) {

			lock (_lockObject) {
				_writerQueueSet.Finish(key);

				_pendingWrites.CompleteRequest(message);
				RemovePendingRequest(message.CorrelationId);

				WorkQueue(key, writer);
			}
		}

		public void QueuePendingWrite(
			Guid key,
			Guid corrId,
			Action<ClientMessage.WriteEventsCompleted> action,
			ClientMessage.WriteEvents message,
			RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted> writer) {

			lock (_lockObject) {
				AddPendingRequest(corrId);
				_pendingWrites.CaptureCallback(corrId, action);

				_writerQueueSet.AddToQueue(key, message);

				WorkQueue(key, writer);
			}
		}
	}

	public const int ReadTimeoutMs = 10000;

	private readonly Guid _selfId = Guid.NewGuid();
	private readonly IPublisher _publisher;
	private readonly IEnvelope _inputQueueEnvelope;
	private readonly RequestTracking _requestTracker;

	public readonly
		RequestResponseDispatcher
		<ClientMessage.ReadStreamEventsForward, ClientMessage.ReadStreamEventsForwardCompleted> ForwardReader;

	public ReadDispatcher BackwardReader { get; }

	public readonly RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted> Writer;

	public readonly RequestResponseDispatcher<ClientMessage.DeleteStream, ClientMessage.DeleteStreamCompleted>
		StreamDeleter;

	public readonly RequestResponseDispatcher<AwakeServiceMessage.SubscribeAwake, IODispatcherDelayedMessage>
		Awaker;

	public readonly RequestResponseDispatcher<ClientMessage.ReadEvent, ClientMessage.ReadEventCompleted> EventReader;

	public readonly
		RequestResponseDispatcher
		<ClientMessage.ReadAllEventsForward, ClientMessage.ReadAllEventsForwardCompleted> AllForwardReader;

	public readonly
		RequestResponseDispatcher
		<ClientMessage.ReadAllEventsBackward, ClientMessage.ReadAllEventsBackwardCompleted> AllBackwardReader;

	public readonly
		RequestResponseDispatcher
		<ClientMessage.FilteredReadAllEventsForward, ClientMessage.FilteredReadAllEventsForwardCompleted> AllForwardFilteredReader;

	public readonly
		RequestResponseDispatcher
		<ClientMessage.FilteredReadAllEventsBackward, ClientMessage.FilteredReadAllEventsBackwardCompleted> AllBackwardFilteredReader;

	public IODispatcher(IPublisher publisher, IEnvelope envelope, bool trackPendingRequests = false) {
		_publisher = publisher;
		_inputQueueEnvelope = envelope;
		_requestTracker = new RequestTracking(trackPendingRequests);

		ForwardReader =
			new RequestResponseDispatcher
				<ClientMessage.ReadStreamEventsForward, ClientMessage.ReadStreamEventsForwardCompleted>(
					publisher,
					v => v.CorrelationId,
					v => v.CorrelationId,
					envelope);

		BackwardReader = new ReadDispatcher(
			publisher,
			v => v.CorrelationId,
			v => v.CorrelationId,
			v => v.CorrelationId,
			envelope);

		Writer =
			new RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted>(
				publisher,
				v => v.CorrelationId,
				v => v.CorrelationId,
				envelope);

		StreamDeleter =
			new RequestResponseDispatcher<ClientMessage.DeleteStream, ClientMessage.DeleteStreamCompleted>(
				publisher,
				v => v.CorrelationId,
				v => v.CorrelationId,
				envelope);

		Awaker =
			new RequestResponseDispatcher<AwakeServiceMessage.SubscribeAwake, IODispatcherDelayedMessage>(
				publisher,
				v => v.CorrelationId,
				v => v.CorrelationId,
				envelope,
				cancelMessageFactory: requestId => new AwakeServiceMessage.UnsubscribeAwake(requestId));

		EventReader =
			new RequestResponseDispatcher<ClientMessage.ReadEvent, ClientMessage.ReadEventCompleted>(
				publisher,
				v => v.CorrelationId,
				v => v.CorrelationId,
				envelope);

		AllForwardReader =
			new RequestResponseDispatcher
				<ClientMessage.ReadAllEventsForward, ClientMessage.ReadAllEventsForwardCompleted>(
					publisher,
					v => v.CorrelationId,
					v => v.CorrelationId,
					envelope);

		AllBackwardReader =
			new RequestResponseDispatcher
				<ClientMessage.ReadAllEventsBackward, ClientMessage.ReadAllEventsBackwardCompleted>(
					publisher,
					v => v.CorrelationId,
					v => v.CorrelationId,
					envelope);

		AllForwardFilteredReader =
			new RequestResponseDispatcher
				<ClientMessage.FilteredReadAllEventsForward, ClientMessage.FilteredReadAllEventsForwardCompleted>(
					publisher,
					v => v.CorrelationId,
					v => v.CorrelationId,
					envelope);

		AllBackwardFilteredReader =
			new RequestResponseDispatcher
				<ClientMessage.FilteredReadAllEventsBackward, ClientMessage.FilteredReadAllEventsBackwardCompleted>(
					publisher,
					v => v.CorrelationId,
					v => v.CorrelationId,
					envelope);
	}

	public void StartDraining(Action onRequestsDrained) {
		_requestTracker.StartDraining(onRequestsDrained);
	}

	private void AddPendingRequest(Guid correlationId) {
		_requestTracker.AddPendingRequest(correlationId);
	}

	private void RemovePendingRequest(Guid correlationId) {
		_requestTracker.RemovePendingRequest(correlationId);
	}

	public Guid ReadBackward(
		string streamId,
		long fromEventNumber,
		int maxCount,
		bool resolveLinks,
		ClaimsPrincipal principal,
		Action<ClientMessage.ReadStreamEventsBackwardCompleted> action,
		Guid? corrId = null,
		DateTime? expires = null) {
		if (!corrId.HasValue)
			corrId = Guid.NewGuid();

		return BackwardReader.Publish(
			new ClientMessage.ReadStreamEventsBackward(
					corrId.Value,
					corrId.Value,
					BackwardReader.Envelope,
					streamId,
					fromEventNumber,
					maxCount,
					resolveLinks,
					false,
					null,
					principal,
					expires: expires),
			new ReadStreamEventsBackwardHandlers.Optimistic(action));
	}

	public Guid ReadBackward(
		string streamId,
		long fromEventNumber,
		int maxCount,
		bool resolveLinks,
		ClaimsPrincipal principal,
		Action<ClientMessage.ReadStreamEventsBackwardCompleted> action,
		Action timeoutAction,
		Guid corrId) {

		var handler = new ReadStreamEventsBackwardHandlers.Tracking(
			corrId,
			_requestTracker,
			new ReadStreamEventsBackwardHandlers.AdHoc(
				handled: action,
				notHandled: null,
				timedout: timeoutAction));

		BackwardReader.Publish(
			new ClientMessage.ReadStreamEventsBackward(
				corrId,
				corrId,
				BackwardReader.Envelope,
				streamId,
				fromEventNumber,
				maxCount,
				resolveLinks,
				false,
				null,
				principal),
			handler);

		Delay(TimeSpan.FromMilliseconds(ReadTimeoutMs), BackwardReader, corrId);
		return corrId;
	}

	public Guid ReadBackward(
		string streamId,
		long fromEventNumber,
		int maxCount,
		bool resolveLinks,
		ClaimsPrincipal principal,
		IReadStreamEventsBackwardHandler handler,
		Guid corrId) {

		var trackingHandler = new ReadStreamEventsBackwardHandlers.Tracking(
			corrId, _requestTracker, handler);

		BackwardReader.Publish(
			new ClientMessage.ReadStreamEventsBackward(
				corrId,
				corrId,
				BackwardReader.Envelope,
				streamId,
				fromEventNumber,
				maxCount,
				resolveLinks,
				false,
				null,
				principal),
			trackingHandler);

		Delay(TimeSpan.FromMilliseconds(ReadTimeoutMs), BackwardReader, corrId);
		return corrId;
	}

	public Guid ReadForward(
		string streamId,
		long fromEventNumber,
		int maxCount,
		bool resolveLinks,
		ClaimsPrincipal principal,
		Action<ClientMessage.ReadStreamEventsForwardCompleted> action,
		Guid? corrId = null) {
		if (!corrId.HasValue)
			corrId = Guid.NewGuid();
		return
			ForwardReader.Publish(
				new ClientMessage.ReadStreamEventsForward(
					corrId.Value,
					corrId.Value,
					ForwardReader.Envelope,
					streamId,
					fromEventNumber,
					maxCount,
					resolveLinks,
					false,
					null,
					principal,
					replyOnExpired: false),
				action);
	}

	public Guid ReadForward(
		string streamId,
		long fromEventNumber,
		int maxCount,
		bool resolveLinks,
		ClaimsPrincipal principal,
		Action<ClientMessage.ReadStreamEventsForwardCompleted> action,
		Action timeoutAction,
		Guid corrId) {
		_requestTracker.AddPendingRead(corrId);

		ForwardReader.Publish(
			new ClientMessage.ReadStreamEventsForward(
				corrId,
				corrId,
				ForwardReader.Envelope,
				streamId,
				fromEventNumber,
				maxCount,
				resolveLinks,
				false,
				null,
				principal,
				replyOnExpired: false),
			res => {
				if (_requestTracker.RemovePendingRead(res.CorrelationId)) {
					action(res);
				}
			},
			() => {
				if (_requestTracker.RemovePendingRead(corrId)) {
					timeoutAction();
				}
			});
		Delay(TimeSpan.FromMilliseconds(ReadTimeoutMs), ForwardReader, corrId);
		return corrId;
	}

	public Guid ReadEvent(
		string streamId,
		long fromEventNumber,
		ClaimsPrincipal principal,
		Action<ClientMessage.ReadEventCompleted> action,
		Action timeoutAction,
		Guid corrId) {
		_requestTracker.AddPendingRead(corrId);

		EventReader.Publish(
			new ClientMessage.ReadEvent(
				corrId,
				corrId,
				EventReader.Envelope,
				streamId,
				fromEventNumber,
				false,
				false,
				principal),
			res => {
				if (_requestTracker.RemovePendingRead(res.CorrelationId)) {
					action(res);
				}
			},
			() => {
				if (_requestTracker.RemovePendingRead(corrId)) {
					timeoutAction();
				}
			});
		Delay(TimeSpan.FromMilliseconds(ReadTimeoutMs), EventReader, corrId);
		return corrId;
	}

	public Guid ReadAllForward(
		long commitPosition,
		long preparePosition,
		int maxCount,
		bool resolveLinks,
		bool requireLeader,
		long? validationTfLastCommitPosition,
		ClaimsPrincipal user,
		TimeSpan? longPollTimeout,
		Action<ClientMessage.ReadAllEventsForwardCompleted> action,
		Action timeoutAction,
		Guid corrId) {
		_requestTracker.AddPendingRead(corrId);

		AllForwardReader.Publish(
			new ClientMessage.ReadAllEventsForward(
				corrId,
				corrId,
				AllForwardReader.Envelope,
				commitPosition,
				preparePosition,
				maxCount,
				resolveLinks,
				requireLeader,
				validationTfLastCommitPosition,
				user,
				replyOnExpired: false,
				longPollTimeout: longPollTimeout
				),
			res => {
				if (_requestTracker.RemovePendingRead(res.CorrelationId)) {
					action(res);
				}
			},
			() => {
				if (_requestTracker.RemovePendingRead(corrId)) {
					timeoutAction();
				}
			});
		Delay(TimeSpan.FromMilliseconds(ReadTimeoutMs), AllForwardReader, corrId);
		return corrId;
	}

	public Guid ReadAllBackward(
		long commitPosition,
		long preparePosition,
		int maxCount,
		bool resolveLinks,
		bool requireLeader,
		long? validationTfLastCommitPosition,
		ClaimsPrincipal user,
		TimeSpan? longPollTimeout,
		Action<ClientMessage.ReadAllEventsBackwardCompleted> action,
		Action timeoutAction,
		Guid corrId) {
		_requestTracker.AddPendingRead(corrId);

		AllBackwardReader.Publish(
			new ClientMessage.ReadAllEventsBackward(
				corrId,
				corrId,
				AllBackwardReader.Envelope,
				commitPosition,
				preparePosition,
				maxCount,
				resolveLinks,
				requireLeader,
				validationTfLastCommitPosition,
				user
			),
			res => {
				if (_requestTracker.RemovePendingRead(res.CorrelationId)) {
					action(res);
				}
			},
			() => {
				if (_requestTracker.RemovePendingRead(corrId)) {
					timeoutAction();
				}
			});
		Delay(TimeSpan.FromMilliseconds(ReadTimeoutMs), AllBackwardReader, corrId);
		return corrId;
	}

	public Guid ReadAllForwardFiltered(
		long commitPosition,
		long preparePosition,
		int maxCount,
		bool resolveLinks,
		bool requireLeader,
		int maxSearchWindow,
		long? validationTfLastCommitPosition,
		IEventFilter eventFilter,
		ClaimsPrincipal user,
		TimeSpan? longPollTimeout,
		Action<ClientMessage.FilteredReadAllEventsForwardCompleted> action,
		Action timeoutAction,
		Guid corrId) {
		_requestTracker.AddPendingRead(corrId);

		AllForwardFilteredReader.Publish(
			new ClientMessage.FilteredReadAllEventsForward(
				corrId,
				corrId,
				AllForwardFilteredReader.Envelope,
				commitPosition,
				preparePosition,
				maxCount,
				resolveLinks,
				requireLeader,
				maxSearchWindow,
				validationTfLastCommitPosition,
				eventFilter,
				user,
				replyOnExpired: false,
				longPollTimeout: longPollTimeout
			),
			res => {
				if (_requestTracker.RemovePendingRead(res.CorrelationId)) {
					action(res);
				}
			},
			() => {
				if (_requestTracker.RemovePendingRead(corrId)) {
					timeoutAction();
				}
			});
		Delay(TimeSpan.FromMilliseconds(ReadTimeoutMs), AllForwardFilteredReader, corrId);
		return corrId;
	}

	public Guid ReadAllBackwardFiltered(
		long commitPosition,
		long preparePosition,
		int maxCount,
		bool resolveLinks,
		bool requireLeader,
		int maxSearchWindow,
		long? validationTfLastCommitPosition,
		IEventFilter eventFilter,
		ClaimsPrincipal user,
		Action<ClientMessage.FilteredReadAllEventsBackwardCompleted> action,
		Action timeoutAction,
		Guid corrId) {
		_requestTracker.AddPendingRead(corrId);

		AllBackwardFilteredReader.Publish(
			new ClientMessage.FilteredReadAllEventsBackward(
				corrId,
				corrId,
				AllBackwardFilteredReader.Envelope,
				commitPosition,
				preparePosition,
				maxCount,
				resolveLinks,
				requireLeader,
				maxSearchWindow,
				validationTfLastCommitPosition,
				eventFilter,
				user
			),
			res => {
				if (_requestTracker.RemovePendingRead(res.CorrelationId)) {
					action(res);
				}
			},
			() => {
				if (_requestTracker.RemovePendingRead(corrId)) {
					timeoutAction();
				}
			});
		Delay(TimeSpan.FromMilliseconds(ReadTimeoutMs), AllBackwardFilteredReader, corrId);
		return corrId;
	}

	public void ConfigureStreamAndWriteEvents(
		string streamId,
		long expectedVersion,
		Lazy<StreamMetadata> streamMetadata,
		Event[] events,
		ClaimsPrincipal principal,
		Action<ClientMessage.WriteEventsCompleted> action) {
		if (expectedVersion != ExpectedVersion.Any && expectedVersion != ExpectedVersion.NoStream)
			WriteEvents(streamId, expectedVersion, events, principal, action);
		else
			ReadBackward(
				streamId,
				-1,
				1,
				false,
				principal,
				completed => {
					switch (completed.Result) {
						case ReadStreamResult.Success:
						case ReadStreamResult.NoStream:
							if (completed.Events is not null && completed.Events.Count > 0)
								WriteEvents(streamId, expectedVersion, events, principal, action);
							else
								UpdateStreamAcl(
									streamId,
									ExpectedVersion.Any,
									principal,
									streamMetadata.Value,
									metaCompleted =>
										WriteEvents(streamId, expectedVersion, events, principal, action));
							break;
						case ReadStreamResult.AccessDenied:
							action(
								new ClientMessage.WriteEventsCompleted(
									Guid.NewGuid(),
									OperationResult.AccessDenied,
									""));
							break;
						case ReadStreamResult.StreamDeleted:
							action(
								new ClientMessage.WriteEventsCompleted(
									Guid.NewGuid(),
									OperationResult.StreamDeleted,
									""));
							break;
						default:
							throw new NotSupportedException();
					}
				});
	}

	public Guid WriteEvents(
		string streamId,
		long expectedVersion,
		Event[] events,
		ClaimsPrincipal principal,
		Action<ClientMessage.WriteEventsCompleted> action) {
		var corrId = Guid.NewGuid();
		AddPendingRequest(corrId);
		return
			Writer.Publish(
				new ClientMessage.WriteEvents(
					corrId,
					corrId,
					Writer.Envelope,
					false,
					streamId,
					expectedVersion,
					events,
					principal),
				res => {
					RemovePendingRequest(res.CorrelationId);
					action(res);
				});
	}

	private class PendingWrites {
		private readonly Dictionary<Guid, Action<ClientMessage.WriteEventsCompleted>> _map;

		public PendingWrites() {
			_map = new Dictionary<Guid, Action<ClientMessage.WriteEventsCompleted>>();
		}

		public void CaptureCallback(Guid correlationId, Action<ClientMessage.WriteEventsCompleted> action) {
			_map.Add(correlationId, action);
		}

		public void CompleteRequest(ClientMessage.WriteEventsCompleted message) {
			Action<ClientMessage.WriteEventsCompleted> action;
			if (_map.TryGetValue(message.CorrelationId, out action)) {
				_map.Remove(message.CorrelationId);
				action(message);
			}
		}
	}

	private class PendingReads {
		private readonly HashSet<Guid> _pendingReads = new HashSet<Guid>();

		public void Register(Guid id) {
			_pendingReads.Add(id);
		}

		public bool IsRegistered(Guid id) {
			var ret = _pendingReads.Contains(id);
			return ret;
		}

		public void Remove(Guid id) {
			_pendingReads.Remove(id);
		}
	}

	private class WriterQueueSet {
		private readonly Dictionary<Guid, WriterQueue> _queues;

		public WriterQueueSet() {
			_queues = new Dictionary<Guid, WriterQueue>();
		}

		public void AddToQueue(Guid key, ClientMessage.WriteEvents message) {
			WriterQueue writerQueue;
			if (!_queues.TryGetValue(key, out writerQueue)) {
				writerQueue = new WriterQueue();
				_queues.Add(key, writerQueue);
			}

			writerQueue.Enqueue(message);
		}

		public void Finish(Guid key) {
			var queue = GetQueue(key);
			if (queue == null)
				return;
			queue.IsBusy = false;

			CleanupQueue(key, queue);
		}

		public bool IsBusy(Guid key) =>
			GetQueue(key)?.IsBusy ?? false;

		public bool HasPendingWrites(Guid key) =>
			GetQueue(key)?.Count > 0;

		public ClientMessage.WriteEvents Dequeue(Guid key) =>
			GetQueue(key)?.Dequeue();

		private WriterQueue GetQueue(Guid key) {
			WriterQueue queue;
			_queues.TryGetValue(key, out queue);
			return queue;
		}

		private void CleanupQueue(Guid key, WriterQueue queue) {
			if (queue.IsBusy)
				return;
			if (queue.Count > 0)
				return;
			_queues.Remove(key);
		}
	}

	private class WriterQueue {
		private readonly Queue<ClientMessage.WriteEvents> _queue;
		public bool IsBusy;
		public int Count => _queue.Count;

		public WriterQueue() {
			IsBusy = false;
			_queue = new Queue<ClientMessage.WriteEvents>();
		}

		public void Enqueue(ClientMessage.WriteEvents message) {
			_queue.Enqueue(message);
		}

		public ClientMessage.WriteEvents Dequeue() {
			if (_queue.Count == 0)
				return null;

			IsBusy = true;

			return _queue.Dequeue();
		}
	}

	public Guid QueueWriteEvents(
		Guid key,
		string streamId,
		long expectedVersion,
		Event[] events,
		ClaimsPrincipal principal,
		Action<ClientMessage.WriteEventsCompleted> action) {
		var corrId = Guid.NewGuid();

		var message = new ClientMessage.WriteEvents(
			corrId,
			corrId,
			Writer.Envelope,
			false,
			streamId,
			expectedVersion,
			events,
			principal);
		_requestTracker.QueuePendingWrite(key, corrId, action, message, Writer);

		return corrId;
	}

	public Guid WriteEvent(
		string streamId,
		long expectedVersion,
		Event @event,
		ClaimsPrincipal principal,
		Action<ClientMessage.WriteEventsCompleted> action) {
		var corrId = Guid.NewGuid();
		AddPendingRequest(corrId);
		return
			Writer.Publish(
				new ClientMessage.WriteEvents(
					corrId,
					corrId,
					Writer.Envelope,
					false,
					streamId,
					expectedVersion,
					new[] { @event },
					principal),
				res => {
					RemovePendingRequest(res.CorrelationId);
					action(res);
				});
	}

	public Guid DeleteStream(
		string streamId,
		long expectedVersion,
		bool hardDelete,
		ClaimsPrincipal principal,
		Action<ClientMessage.DeleteStreamCompleted> action) {
		var corrId = Guid.NewGuid();
		AddPendingRequest(corrId);
		return StreamDeleter.Publish(
			new ClientMessage.DeleteStream(
				corrId,
				corrId,
				Writer.Envelope,
				false,
				streamId,
				expectedVersion,
				hardDelete,
				principal),
			res => {
				RemovePendingRequest(res.CorrelationId);
				action(res);
			});
	}

	public void SubscribeAwake(
		string streamId,
		TFPos from,
		Action<IODispatcherDelayedMessage> action,
		Guid? correlationId = null) {
		var corrId = correlationId ?? Guid.NewGuid();
		AddPendingRequest(corrId);
		Awaker.Publish(
			new AwakeServiceMessage.SubscribeAwake(
				Awaker.Envelope,
				corrId,
				streamId,
				from,
				new IODispatcherDelayedMessage(corrId, null)),
			res => {
				RemovePendingRequest(res.CorrelationId);
				action(res);
			});
	}

	public void UnsubscribeAwake(Guid correlationId) {
		Awaker.Cancel(correlationId);
	}

	public void UpdateStreamAcl(
		string streamId,
		long expectedVersion,
		ClaimsPrincipal principal,
		StreamMetadata metadata,
		Action<ClientMessage.WriteEventsCompleted> completed) {
		WriteEvents(
			SystemStreams.MetastreamOf(streamId),
			expectedVersion,
			new[] { new Event(Guid.NewGuid(), SystemEventTypes.StreamMetadata, true, metadata.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None) },
			principal,
			completed);
	}

	public void Delay(TimeSpan delay, Action<Guid> timeout) {
		_publisher.Publish(
			TimerMessage.Schedule.Create(
				delay,
				_inputQueueEnvelope,
				new IODispatcherDelayedMessage(_selfId, new AdHocCorrelatedTimeout(timeout))));
	}

	private void Delay(TimeSpan delay, ICorrelatedTimeout timeout, Guid messageCorrelationId) {
		_publisher.Publish(
			TimerMessage.Schedule.Create(
				delay,
				_inputQueueEnvelope,
				new IODispatcherDelayedMessage(_selfId, timeout, messageCorrelationId)));
	}

	public void Handle(IODispatcherDelayedMessage message) {
		if (_selfId != message.CorrelationId)
			return;
		message.Timeout();
	}

	public void Handle(ClientMessage.NotHandled message) {
		// we do not remove the pending read here but only the pending request.
		// the pending read will be removed when calling the timeout action
		_requestTracker.RemovePendingRequest(message.CorrelationId);
	}
}
