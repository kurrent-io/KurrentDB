// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.POC.IO.Core;
using Serilog;
using CommonPosition = KurrentDB.Core.Services.Transport.Common.Position;
using Event = KurrentDB.POC.IO.Core.Event;
using Position = KurrentDB.POC.IO.Core.Position;

namespace KurrentDB.POC.ConnectedSubsystemsPlugin;

// this provides a client interface
public class InternalClient : IClient {
	private static readonly ILogger Log = Serilog.Log.ForContext<InternalClient>();

	private const int MaxLiveEventBufferCount = 32;

	private readonly IPublisher _publisher;
	private readonly bool _requiresLeader;

	private static readonly BoundedChannelOptions _boundedChannelOptions =
		new(MaxLiveEventBufferCount) {
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
			SingleWriter = true
		};

	public InternalClient(IPublisher publisher, bool requiresLeader = false) {
		_publisher = publisher;
		_requiresLeader = requiresLeader;
	}

	public async Task WriteMetaDataMaxCountAsync(string stream, CancellationToken cancellationToken) {
		var metadata = new StreamMetadata(maxCount: 3);
		var data = metadata.ToJsonBytes();
		var eventToWrite = new EventToWrite(Guid.NewGuid(), SystemEventTypes.StreamMetadata, "application/json", data,
			null);

		await WriteAsync(SystemStreams.MetastreamOf(stream), new[] { eventToWrite }, -2, cancellationToken);
	}

	public async Task<long> WriteAsync(string stream, EventToWrite[] events, long expectedVersion, CancellationToken cancellationToken) {

		var appendResponseSource = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
		var envelope = new CallbackEnvelope(HandleWriteEventsCompleted);

		//qq consider all the args
		var correlationId = Guid.NewGuid(); //qq
		_publisher.Publish(new ClientMessage.WriteEvents(
			correlationId,
			correlationId,
			envelope,
			requireLeader: _requiresLeader,
			stream,
			expectedVersion,
			events.Select(evt =>
				new Core.Data.Event(
					evt.EventId, evt.EventType, isJson: evt.ContentType == "application/json",
					evt.Data.ToArray(),
					evt.Metadata.ToArray(), SchemaInfo.None, SchemaInfo.None)).ToArray(),
			SystemAccounts.System,
			cancellationToken: cancellationToken));

		return await appendResponseSource.Task.ConfigureAwait(false);

		void HandleWriteEventsCompleted(Message message) {
			if (message is ClientMessage.NotHandled notHandled) {
				Exception ex = notHandled.Reason switch {
					ClientMessage.NotHandled.Types.NotHandledReason.NotReady => new ResponseException.ServerNotReady(),
					ClientMessage.NotHandled.Types.NotHandledReason.TooBusy => new ResponseException.ServerBusy(),
					ClientMessage.NotHandled.Types.NotHandledReason.NotLeader or
					ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly => new ResponseException.NotLeader(),
					_ => new ResponseException.UnexpectedError($"NotHandled {notHandled.Reason}"),
				};
				appendResponseSource.TrySetException(ex);
				return;
			}

			if (message is not ClientMessage.WriteEventsCompleted completed) {
				appendResponseSource.TrySetException(new ResponseException.UnexpectedError($"Unexpected message {message}"));
				return;
			}

			//qqqqqqqq
			switch (completed.Result) {
				case OperationResult.Success:
					appendResponseSource.TrySetResult(completed.LastEventNumber);
					return;
				case OperationResult.PrepareTimeout:
				case OperationResult.CommitTimeout:
				case OperationResult.ForwardTimeout:
					appendResponseSource.TrySetException(new ResponseException.Timeout(completed.Message));
					return;
				case OperationResult.WrongExpectedVersion:
					//qq irl this is much more complicated see Streams.Append.cs
					appendResponseSource.TrySetException(new ResponseException.WrongExpectedVersion(completed.Message));
					return;
				case OperationResult.StreamDeleted:
					appendResponseSource.TrySetException(new ResponseException.StreamDeleted(completed.Message));
					return;
				case OperationResult.InvalidTransaction:
					appendResponseSource.TrySetException(new ResponseException.UnexpectedError(completed.Message));
					return;
				case OperationResult.AccessDenied:
					appendResponseSource.TrySetException(new ResponseException.AccessDenied(completed.Message));
					return;
				default:
					appendResponseSource.TrySetException(new ResponseException.UnexpectedError(completed.Message));
					return;
			}
		}
	}

	private static CommonPosition Convert(FromAll start) {
		if (start == FromAll.Start)
			return CommonPosition.Start;
		else if (start == FromAll.End)
			return CommonPosition.End;
		else
			return Convert(start.Position!.Value);
	}

	private static CommonPosition Convert(Position position) =>
		new(position.CommitPosition, position.PreparePosition);

	public IAsyncEnumerable<Event> SubscribeToAll(FromAll start, CancellationToken token) =>
		//qq consider all these options
		Create(
			"SUBSCRIPTION TO $all",
			() => new Enumerator.AllSubscription(
				bus: _publisher,
				expiryStrategy: new DefaultExpiryStrategy(),
				checkpoint: Convert(start),
				resolveLinks: false,
				user: SystemAccounts.System,
				requiresLeader: _requiresLeader,
				cancellationToken: token));

	public IAsyncEnumerable<Event> SubscribeToStream(string stream, CancellationToken token) =>
		Create(
			$"SUBSCRIPTION TO {stream}",
			() => new Enumerator.StreamSubscription<string>(
				streamName: stream,
				bus: _publisher,
				expiryStrategy: new DefaultExpiryStrategy(),
				checkpoint: null,
				resolveLinks: false,
				user: SystemAccounts.System,
				requiresLeader: _requiresLeader,
				cancellationToken: token));

	public IAsyncEnumerable<Event> ReadStreamBackwards(string stream, long maxCount, CancellationToken token) =>
		Create(
			$"Reading stream {stream} backwards for max {maxCount} events",
			() => new Enumerator.ReadStreamBackwards(
				bus: _publisher,
				streamName: stream,
				startRevision: StreamRevision.End,
				maxCount: (ulong)maxCount,
				resolveLinks: false,
				user: SystemAccounts.System,
				requiresLeader: _requiresLeader,
				deadline: DateTime.UtcNow.AddSeconds(10),
				cancellationToken: token,
				compatibility: 1));

	public IAsyncEnumerable<Event> ReadAllBackwardsAsync(Position position, long maxCount, CancellationToken token) =>
		Create($"Reading from $all Backwards", () =>
			new Enumerator.ReadAllBackwards(
				bus: _publisher,
				position: Convert(position),
				maxCount: (ulong)maxCount,
				resolveLinks: false,
				user: SystemAccounts.System,
				requiresLeader: _requiresLeader,
				deadline: DateTime.UtcNow.AddSeconds(10),
				cancellationToken: token
			));

	public IAsyncEnumerable<Event> ReadStreamForwards(string stream, long maxCount, CancellationToken token) =>
		Create(
			$"Reading stream {stream} forwards for max {maxCount} events",
			() => new Enumerator.ReadStreamForwards(
				bus: _publisher,
				streamName: stream,
				startRevision: StreamRevision.Start,
				maxCount: (ulong)maxCount,
				resolveLinks: false,
				user: SystemAccounts.System,
				requiresLeader: _requiresLeader,
				deadline: DateTime.UtcNow.AddSeconds(10),
				cancellationToken: token,
				compatibility: 1));

	public async Task DeleteStreamAsync(string stream, long expectedVersion, CancellationToken cancellationToken) {
		var appendResponseSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var envelope = new CallbackEnvelope(HandleDeleteStreamCompleted);

		var correlationId = Guid.NewGuid();
		_publisher.Publish(new ClientMessage.DeleteStream(
			correlationId,
			correlationId,
			envelope,
			requireLeader: _requiresLeader,
			stream,
			expectedVersion,
			hardDelete: false,
			SystemAccounts.System,
			cancellationToken: cancellationToken));

		await appendResponseSource.Task.ConfigureAwait(false);

		void HandleDeleteStreamCompleted(Message message) {
			if (message is ClientMessage.NotHandled notHandled) {
				Exception ex = notHandled.Reason switch {
					ClientMessage.NotHandled.Types.NotHandledReason.NotReady => new ResponseException.ServerNotReady(),
					ClientMessage.NotHandled.Types.NotHandledReason.TooBusy => new ResponseException.ServerBusy(),
					ClientMessage.NotHandled.Types.NotHandledReason.NotLeader or
					ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly => new ResponseException.NotLeader(),
					_ => new ResponseException.UnexpectedError($"NotHandled {notHandled.Reason}"),
				};
				appendResponseSource.TrySetException(ex);
				return;
			}

			if (message is not ClientMessage.DeleteStreamCompleted completed) {
				appendResponseSource.TrySetException(new ResponseException.UnexpectedError($"Unexpected message {message}"));
				return;
			}

			switch (completed.Result) {
				case OperationResult.Success:
					appendResponseSource.TrySetResult();
					return;
				case OperationResult.PrepareTimeout:
				case OperationResult.CommitTimeout:
				case OperationResult.ForwardTimeout:
					appendResponseSource.TrySetException(new ResponseException.Timeout(completed.Message));
					return;
				case OperationResult.WrongExpectedVersion:
					appendResponseSource.TrySetException(new ResponseException.WrongExpectedVersion(completed.Message));
					return;
				case OperationResult.StreamDeleted:
					appendResponseSource.TrySetException(new ResponseException.StreamDeleted(completed.Message));
					return;
				case OperationResult.InvalidTransaction:
					appendResponseSource.TrySetException(new ResponseException.UnexpectedError(completed.Message));
					return;
				case OperationResult.AccessDenied:
					appendResponseSource.TrySetException(new ResponseException.AccessDenied(completed.Message));
					return;
				default:
					appendResponseSource.TrySetException(new ResponseException.UnexpectedError(completed.Message));
					return;
			}
		}
	}

	private static Event ConvertEvent(ref ResolvedEvent evt) {
		var e = evt.OriginalEvent;
		return new Event(
			eventId: e.EventId,
			created: e.TimeStamp,
			stream: e.EventStreamId,
			eventNumber: (ulong)e.EventNumber,
			eventType: e.EventType,
			contentType: e.IsJson ? "application/json" : "application/octet-stream",
			commitPosition: (ulong)evt.OriginalPosition!.Value.CommitPosition,
			preparePosition: (ulong)evt.OriginalPosition!.Value.PreparePosition,
			isRedacted: e.Flags.HasAllOf(PrepareFlags.IsRedacted),
			data: e.Data,
			metadata: e.Metadata);
	}

	private static IAsyncEnumerable<Event> Create(
		string message,
		Func<IAsyncEnumerator<ReadResponse>> enumeratorFactory) {

		return new LoggingEnumerable<ReadResponse>(enumeratorFactory, message)
			.MapExceptions()
			.Select(x => {
				if (x is ReadResponse.StreamNotFound y)
					throw new ResponseException.StreamNotFound(y.StreamName);
				return x;
			})
			.OfType<ReadResponse.EventReceived>()
			.Select(x => ConvertEvent(ref x.Event));
	}

	class LoggingEnumerable<T> : IAsyncEnumerable<T> {
		private readonly Func<IAsyncEnumerator<T>> _enumeratorFactory;
		private readonly string _message;

		public LoggingEnumerable(Func<IAsyncEnumerator<T>> enumeratorFactory, string message) {
			_enumeratorFactory = enumeratorFactory;
			_message = message;
		}

		public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
			return new LoggingEnumerator<T>(_enumeratorFactory(), _message);
		}
	}

	sealed class LoggingEnumerator<T> : IAsyncEnumerator<T> {
		private readonly string _message;
		private readonly IAsyncEnumerator<T> _wrapped;

		public LoggingEnumerator(IAsyncEnumerator<T> wrapped, string message) {
			_wrapped = wrapped;
			_message = message;
			Log.Information("Internal Client: Started {message}", _message);
		}

		public T Current => _wrapped.Current;

		public ValueTask DisposeAsync() {
			Log.Information("Internal Client: Ended {message}", _message);
			return _wrapped.DisposeAsync();
		}

		public ValueTask<bool> MoveNextAsync() {
			return _wrapped.MoveNextAsync();
		}
	}
}

public static class IAsyncEnumerableExtenions {
	public static bool TryMap(this Exception ex, out Exception mapped) {
		mapped = ex.Map();
		return mapped != ex;
	}

	private static Exception Map(this Exception ex) => ex switch {
		ReadResponseException.AccessDenied =>
			new ResponseException.AccessDenied(ex),

		ReadResponseException.NotHandled.ServerNotReady =>
			new ResponseException.ServerNotReady(ex),

		ReadResponseException.NotHandled.ServerBusy =>
			new ResponseException.ServerBusy(ex),

		ReadResponseException.NotHandled.LeaderInfo or
		ReadResponseException.NotHandled.NoLeaderInfo =>
			new ResponseException.NotLeader(ex),

		ReadResponseException.StreamDeleted =>
			new ResponseException.StreamDeleted(ex),

		ReadResponseException.Timeout =>
			new ResponseException.Timeout(ex),

		OperationCanceledException =>
			ex,

		ReadResponseException.InvalidPosition or
		ReadResponseException.UnknownError or
		ReadResponseException.UnknownMessage or
		_ =>
			new ResponseException.UnexpectedError(ex),
	};

	public static async IAsyncEnumerable<T> MapExceptions<T>(
		this IAsyncEnumerable<T> self) {

		await Task.Yield();

		await using var enumerator = self.GetAsyncEnumerator();
		while (true) {
			try {
				if (!await enumerator.MoveNextAsync())
					break;

			} catch (Exception ex) when (ex.TryMap(out var mapped)) {
				throw mapped;
			}

			yield return enumerator.Current;
		}
	}
}
