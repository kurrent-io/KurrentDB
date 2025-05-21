// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections;
using System.Security.Claims;
using System.Threading.Channels;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.Core.Services.Transport.Enumerators.ReadResponseException;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.SecondaryIndexing.Readers;

internal static class SyncEnumerator {
	private static readonly BoundedChannelOptions BoundedChannelOptions =
		new(ReadBatchSize) {
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
			SingleWriter = true
		};

	private const int ReadBatchSize = 2048;

	public sealed class ReadAllForwardsFiltered : IEnumerator<ReadResponse> {
		private readonly IPublisher _bus;
		private readonly ulong _maxCount;
		private readonly IEventFilter _eventFilter;
		private readonly ClaimsPrincipal _user;
		private readonly bool _requiresLeader;
		private readonly DateTime _deadline;
		private readonly uint _maxSearchWindow;
		private readonly SemaphoreSlim _semaphore;
		private readonly Channel<ReadResponse> _channel;

		private ReadResponse _current;

		public bool MoveNext() {
			while (_channel.Reader.Completion is { IsCompleted: false, IsCanceled: false }) {
				if (!_channel.Reader.TryRead(out var current)) continue;
				_current = current;

				return true;
			}

			return false;
		}

		public void Reset() {
		}

		object IEnumerator.Current => Current;

		public ReadResponse Current => _current;

		public ReadAllForwardsFiltered(IPublisher bus,
			Position position,
			ulong maxCount,
			IEventFilter? eventFilter,
			ClaimsPrincipal user,
			bool requiresLeader,
			uint? maxSearchWindow,
			DateTime deadline) {
			_bus = bus;
			_maxCount = maxCount > 0 ? maxCount : ulong.MaxValue;
			_eventFilter = eventFilter ?? EventFilter.DefaultAllFilter;
			_user = user;
			_requiresLeader = requiresLeader;
			_maxSearchWindow = maxSearchWindow ?? ReadBatchSize;
			_deadline = deadline;
			_semaphore = new(1, 1);
			_channel = Channel.CreateBounded<ReadResponse>(BoundedChannelOptions);
			_current = null!;

			ReadPage(position);
		}

		private void ReadPage(Position startPosition, ulong readCount = 0) {
			var correlationId = Guid.NewGuid();

			var (commitPosition, preparePosition) = startPosition.ToInt64();

			_bus.Publish(new FilteredReadAllEventsForward(
				correlationId, correlationId, new ContinuationEnvelope(OnMessage, _semaphore, CancellationToken.None),
				commitPosition, preparePosition, (int)Math.Min(ReadBatchSize, _maxCount), false,
				_requiresLeader, (int)_maxSearchWindow, null, _eventFilter, _user,
				replyOnExpired: false,
				expires: _deadline,
				cancellationToken: CancellationToken.None));

			Task OnMessage(Message message, CancellationToken ct) {
				if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
					_channel.Writer.TryComplete(ex);
					return Task.CompletedTask;
				}

				if (message is not FilteredReadAllEventsForwardCompleted completed) {
					_channel.Writer.TryComplete(UnknownMessage.Create<FilteredReadAllEventsForwardCompleted>(message));
					return Task.CompletedTask;
				}

				switch (completed.Result) {
					case FilteredReadAllResult.Success:
						foreach (var @event in completed.Events) {
							if (readCount >= _maxCount) {
								_channel.Writer.TryComplete();
								return Task.CompletedTask;
							}

							while (!_channel.Writer.TryWrite(new ReadResponse.EventReceived(@event))) { }

							readCount++;
						}

						if (completed.IsEndOfStream) {
							_channel.Writer.TryComplete();
							return Task.CompletedTask;
						}

						Task.Run(
							() => ReadPage(
								Position.FromInt64(completed.NextPos.CommitPosition, completed.NextPos.PreparePosition),
								readCount), ct);
						return Task.CompletedTask;
					case FilteredReadAllResult.AccessDenied:
						_channel.Writer.TryComplete(new AccessDenied());
						return Task.CompletedTask;
					default:
						_channel.Writer.TryComplete(UnknownError.Create(completed.Result));
						return Task.CompletedTask;
				}
			}
		}

		public void Dispose() {
			_channel.Writer.TryComplete();
			_semaphore.Dispose();
		}
	}

	private static bool TryHandleNotHandled(ClientMessage.NotHandled notHandled, out ReadResponseException? exception) {
		exception = null;
		switch (notHandled.Reason) {
			case ClientMessage.NotHandled.Types.NotHandledReason.NotReady:
				exception = new ReadResponseException.NotHandled.ServerNotReady();
				return true;
			case ClientMessage.NotHandled.Types.NotHandledReason.TooBusy:
				exception = new ReadResponseException.NotHandled.ServerBusy();
				return true;
			case ClientMessage.NotHandled.Types.NotHandledReason.NotLeader:
			case ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly:
				switch (notHandled.LeaderInfo) {
					case { } leaderInfo:
						exception = new ReadResponseException.NotHandled.LeaderInfo(leaderInfo.Http.GetHost(),
							leaderInfo.Http.GetPort());
						return true;
					default:
						exception = new ReadResponseException.NotHandled.NoLeaderInfo();
						return true;
				}

			default:
				return false;
		}
	}

	public sealed class ReadStreamForwardsSync : IEnumerator<ReadResponse> {
		private readonly IPublisher _bus;
		private readonly string _streamName;
		private readonly ulong _maxCount;
		private readonly bool _resolveLinks;
		private readonly ClaimsPrincipal _user;
		private readonly bool _requiresLeader;
		private readonly DateTime _deadline;
		private readonly SemaphoreSlim _semaphore;
		private readonly Channel<ReadResponse> _channel;

		private ReadResponse _current;

		public bool MoveNext() {
			while (_channel.Reader.Completion is { IsCompleted: false, IsCanceled: false }) {
				if (!_channel.Reader.TryRead(out var current)) continue;
				_current = current;
				return true;
			}

			return false;
		}

		public void Reset() {
			throw new NotSupportedException();
		}

		object IEnumerator.Current => Current;

		public ReadResponse Current => _current;

		public ReadStreamForwardsSync(IPublisher bus,
			string streamName,
			StreamRevision startRevision,
			ulong maxCount,
			bool resolveLinks,
			ClaimsPrincipal user,
			bool requiresLeader,
			DateTime deadline
		) {
			_bus = bus ?? throw new ArgumentNullException(nameof(bus));
			_streamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
			_maxCount = maxCount;
			_resolveLinks = resolveLinks;
			_user = user;
			_requiresLeader = requiresLeader;
			_deadline = deadline;
			_semaphore = new(1, 1);
			_channel = Channel.CreateBounded<ReadResponse>(BoundedChannelOptions);
			_current = null!;

			ReadPage(startRevision);
		}

		private void ReadPage(StreamRevision startRevision, ulong readCount = 0) {
			Guid correlationId = Guid.NewGuid();

			_bus.Publish(new ReadStreamEventsForward(
				correlationId, correlationId, new ContinuationEnvelope(OnMessage, _semaphore, CancellationToken.None),
				_streamName, startRevision.ToInt64(), (int)Math.Min(ReadBatchSize, _maxCount), _resolveLinks,
				_requiresLeader, null, _user, replyOnExpired: false, expires: _deadline,
				cancellationToken: CancellationToken.None));
			return;

			Task OnMessage(Message message, CancellationToken ct) {
				if (message is ClientMessage.NotHandled notHandled && TryHandleNotHandled(notHandled, out var ex)) {
					_channel.Writer.TryComplete(ex);
					return Task.CompletedTask;
				}

				if (message is not ReadStreamEventsForwardCompleted completed) {
					_channel.Writer.TryComplete(UnknownMessage.Create<ReadStreamEventsForwardCompleted>(message));
					return Task.CompletedTask;
				}

				switch (completed.Result) {
					case ReadStreamResult.Success:
						foreach (var @event in completed.Events) {
							if (readCount >= _maxCount) {
								break;
							}

							_channel.Writer.TryWrite(new ReadResponse.EventReceived(@event));
							readCount++;
						}

						if (!completed.IsEndOfStream && readCount < _maxCount) {
							Task.Run(() => ReadPage(StreamRevision.FromInt64(completed.NextEventNumber), readCount),
								ct);
							return Task.CompletedTask;
						}

						_channel.Writer.TryComplete();
						return Task.CompletedTask;
					case ReadStreamResult.NoStream:
						_channel.Writer.TryWrite(new ReadResponse.StreamNotFound(_streamName));
						_channel.Writer.TryComplete();
						return Task.CompletedTask;
					case ReadStreamResult.StreamDeleted:
						_channel.Writer.TryComplete(new StreamDeleted(_streamName));
						return Task.CompletedTask;
					case ReadStreamResult.AccessDenied:
						_channel.Writer.TryComplete(new AccessDenied());
						return Task.CompletedTask;
					default:
						_channel.Writer.TryComplete(UnknownError.Create(completed.Result));
						return Task.CompletedTask;
				}
			}
		}

		public void Dispose() {
			_semaphore.Dispose();
			_channel.Writer.TryComplete();
		}
	}
}
