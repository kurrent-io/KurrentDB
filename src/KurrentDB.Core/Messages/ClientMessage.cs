// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Settings;
using FilteredReadAllResult = KurrentDB.Core.Data.FilteredReadAllResult;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.Core.Messages;

public enum OperationResult {
	Success = 0,
	PrepareTimeout = 1,
	CommitTimeout = 2,
	ForwardTimeout = 3,
	WrongExpectedVersion = 4,
	StreamDeleted = 5,
	InvalidTransaction = 6,
	AccessDenied = 7
}

public static partial class ClientMessage {
	[DerivedMessage(CoreMessage.Client)]
	public partial class RequestShutdown : Message {
		public readonly bool ExitProcess;

		public readonly bool ShutdownHttp;

		public RequestShutdown(bool exitProcess, bool shutdownHttp) {
			ExitProcess = exitProcess;
			ShutdownHttp = shutdownHttp;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReloadConfig : Message {
	}

	[DerivedMessage]
	public abstract partial class WriteRequestMessage : Message {
		public readonly Guid InternalCorrId;
		public readonly Guid CorrelationId;
		public readonly IEnvelope Envelope;
		public readonly bool RequireLeader;

		public readonly ClaimsPrincipal User;
		public string Login => Tokens?.GetValueOrDefault("uid");
		public string Password => Tokens?.GetValueOrDefault("pwd");
		public readonly IReadOnlyDictionary<string, string> Tokens;

		protected WriteRequestMessage(Guid internalCorrId,
			Guid correlationId, IEnvelope envelope, bool requireLeader,
			ClaimsPrincipal user, IReadOnlyDictionary<string, string> tokens,
			CancellationToken token) : base(token) {

			Ensure.NotEmptyGuid(internalCorrId, "internalCorrId");
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			Ensure.NotNull(envelope, "envelope");

			InternalCorrId = internalCorrId;
			CorrelationId = correlationId;
			Envelope = envelope;
			RequireLeader = requireLeader;

			User = user;
			Tokens = tokens;
		}
	}

	[DerivedMessage]
	public abstract partial class ReadRequestMessage : Message {
		public readonly Guid InternalCorrId;
		public readonly Guid CorrelationId;
		public readonly IEnvelope Envelope;

		public readonly ClaimsPrincipal User;

		public readonly DateTime Expires;

		protected ReadRequestMessage(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			ClaimsPrincipal user, DateTime? expires,
			CancellationToken cancellationToken = default) : base(cancellationToken) {
			Ensure.NotEmptyGuid(internalCorrId, "internalCorrId");
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			Ensure.NotNull(envelope, "envelope");

			InternalCorrId = internalCorrId;
			CorrelationId = correlationId;
			Envelope = envelope;

			User = user;
			Expires = expires ?? DateTime.UtcNow.AddMilliseconds(ESConsts.ReadRequestTimeout);
		}

		public override string ToString() =>
			$"{GetType().Name} " +
			$"InternalCorrId: {InternalCorrId}, " +
			$"CorrelationId: {CorrelationId}, " +
			$"User: {User?.FindFirst(ClaimTypes.Name)?.Value ?? "(anonymous)"}, " +
			$"Envelope: {{ {Envelope} }}, " +
			$"Expires: {Expires}";
	}

	[DerivedMessage]
	public abstract partial class ReadResponseMessage : Message {
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class TcpForwardMessage : Message {
		public readonly Message Message;

		public TcpForwardMessage(Message message) {
			Ensure.NotNull(message, "message");

			Message = message;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class NotHandled : Message {
		public readonly Guid CorrelationId;
		public readonly Types.NotHandledReason Reason;
		public readonly Types.LeaderInfo LeaderInfo;
		public readonly string Description;
		public NotHandled(Guid correlationId,
			Types.NotHandledReason reason,
			Types.LeaderInfo leaderInfo) {
			CorrelationId = correlationId;
			Reason = reason;
			LeaderInfo = leaderInfo;
		}

		public NotHandled(Guid correlationId,
			Types.NotHandledReason reason,
			string description) {
			CorrelationId = correlationId;
			Reason = reason;
			Description = description;
		}


		public static class Types {

			public enum NotHandledReason {
				NotReady,
				TooBusy,
				NotLeader,
				IsReadOnly
			}
			public class LeaderInfo {
				public LeaderInfo(EndPoint externalTcp, bool isSecure, EndPoint http) {
					ExternalTcp = externalTcp;
					IsSecure = isSecure;
					Http = http;
				}

				public bool IsSecure { get; }
				public EndPoint ExternalTcp { get; }
				public EndPoint Http { get; }
			}
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class WriteEvents : WriteRequestMessage {
		// one per Stream being written to
		public readonly LowAllocReadOnlyMemory<string> EventStreamIds;

		// one per Stream being written to
		public readonly LowAllocReadOnlyMemory<long> ExpectedVersions;

		public readonly LowAllocReadOnlyMemory<Event> Events;

		// EventStreamIndexes is     [] => stream of event e == EventStreamIds[0]
		// EventStreamIndexes is not [] => stream of event e == EventStreamIds[EventStreamIndexes[index of e in Events]]
		public readonly LowAllocReadOnlyMemory<int> EventStreamIndexes;

		public WriteEvents(
			Guid internalCorrId,
			Guid correlationId,
			IEnvelope envelope,
			bool requireLeader,
			LowAllocReadOnlyMemory<string> eventStreamIds,
			LowAllocReadOnlyMemory<long> expectedVersions,
			LowAllocReadOnlyMemory<Event> events,
			LowAllocReadOnlyMemory<int> eventStreamIndexes,
			ClaimsPrincipal user,
			IReadOnlyDictionary<string, string> tokens = null,
			CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, requireLeader, user, tokens, cancellationToken) {

			// there must be at least one stream
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventStreamIds.Length, nameof(eventStreamIds));

			// each stream must correspond to an expected version at the same index
			ArgumentOutOfRangeException.ThrowIfNotEqual(expectedVersions.Length, eventStreamIds.Length, nameof(expectedVersions));

			if (eventStreamIndexes.Length is not 0) {
				// when non-empty: eventStreamIndexes maps each event to the index of its stream
				ArgumentOutOfRangeException.ThrowIfNotEqual(eventStreamIndexes.Length, events.Length, nameof(eventStreamIndexes));
			} else {
				// when empty, all events implicitly are for the single stream which is at index 0.
			}

			foreach (var eventStreamId in eventStreamIds.Span) {
				if (SystemStreams.IsInvalidStream(eventStreamId))
					throw new ArgumentOutOfRangeException(nameof(eventStreamIds), $"Invalid stream ID: {eventStreamId}");
			}

			foreach (var expectedVersion in expectedVersions.Span) {
				if (expectedVersion is < ExpectedVersion.StreamExists or ExpectedVersion.Invalid)
					throw new ArgumentOutOfRangeException(nameof(expectedVersions), $"Invalid expected version: {expectedVersion}");
			}

			var nextEventStreamIndex = 0;
			if (eventStreamIndexes.Length is not 0) {
				foreach (var eventStreamIndex in eventStreamIndexes.Span) {
					if (eventStreamIndex < 0 || eventStreamIndex >= eventStreamIds.Length)
						throw new ArgumentOutOfRangeException(nameof(eventStreamIndexes),
							$"Stream index is out of range: {eventStreamIndex}. Number of streams: {eventStreamIds.Length}");

					if (eventStreamIndex == nextEventStreamIndex) {
						nextEventStreamIndex++;
					} else if (eventStreamIndex > nextEventStreamIndex) {
						throw new ArgumentOutOfRangeException(nameof(eventStreamIds),
							"Indexes must be assigned to streams in the order in which they first appear in the list of events being written");
					}
				}
			} else {
				nextEventStreamIndex = 1;
			}

			if (events.Length > 0 && nextEventStreamIndex != eventStreamIds.Length)
				throw new ArgumentOutOfRangeException(nameof(eventStreamIds),
					"Not all streams have events being written to them");

			if (events.Length == 0 && eventStreamIds.Length > 1)
				throw new ArgumentException("Empty writes to multiple streams is not supported");

			EventStreamIds = eventStreamIds;
			ExpectedVersions = expectedVersions;
			Events = events;
			EventStreamIndexes = eventStreamIndexes;
		}

		public static WriteEvents ForSingleStream(
			Guid internalCorrId,
			Guid correlationId,
			IEnvelope envelope,
			bool requireLeader,
			string eventStreamId,
			long expectedVersion,
			LowAllocReadOnlyMemory<Event> events,
			ClaimsPrincipal user,
			IReadOnlyDictionary<string, string> tokens = null,
			CancellationToken cancellationToken = default) {
			return new WriteEvents(
				internalCorrId,
				correlationId,
				envelope,
				requireLeader,
				eventStreamIds: new(eventStreamId),
				expectedVersions: new(expectedVersion),
				events,
				eventStreamIndexes: null,
				user,
				tokens,
				cancellationToken);
		}

		public static WriteEvents ForSingleEvent(
			Guid internalCorrId, Guid correlationId, IEnvelope envelope, bool requireLeader, string eventStreamId, long expectedVersion, Event @event, ClaimsPrincipal user, IReadOnlyDictionary<string, string> tokens = null) {
			return new WriteEvents(
				internalCorrId,
				correlationId,
				envelope,
				requireLeader,
				eventStreamIds: new(eventStreamId),
				expectedVersions: new(expectedVersion),
				events: new(@event),
				eventStreamIndexes: null,
				user,
				tokens);
		}

		public override string ToString() {
			return
				$"WRITE:" +
				$"InternalCorrId: {InternalCorrId}," +
				$"CorrelationId: {CorrelationId}," +
				$"EventStreamIds: {string.Join(", ", EventStreamIds.ToArray())}," + // TODO: use .Span instead of .ToArray() when we move to .NET 10
				$"ExpectedVersions: {string.Join(", ", ExpectedVersions.ToArray())}," +
				$"Events: {Events.Length}" +
				$"EventStreamIndexes: {string.Join(", ", EventStreamIndexes.ToArray())}";
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class WriteEventsCompleted : Message {
		public readonly Guid CorrelationId;
		public readonly OperationResult Result;
		public readonly string Message;
		public readonly LowAllocReadOnlyMemory<long> FirstEventNumbers;
		public readonly LowAllocReadOnlyMemory<long> LastEventNumbers;
		public readonly long PreparePosition;
		public readonly long CommitPosition;
		public readonly LowAllocReadOnlyMemory<int> FailureStreamIndexes;
		public readonly LowAllocReadOnlyMemory<long> FailureCurrentVersions;

		/// <summary>Success constructor</summary>
		public WriteEventsCompleted(
			Guid correlationId,
			LowAllocReadOnlyMemory<long> firstEventNumbers,
			LowAllocReadOnlyMemory<long> lastEventNumbers,
			long preparePosition, long commitPosition) {
			ArgumentOutOfRangeException.ThrowIfNotEqual(firstEventNumbers.Length, lastEventNumbers.Length, nameof(firstEventNumbers));

			for (var i = 0; i < firstEventNumbers.Length; i++) {
				var firstEventNumber = firstEventNumbers.Span[i];
				var lastEventNumber = lastEventNumbers.Span[i];

				if (firstEventNumber < -1)
					throw new ArgumentOutOfRangeException(nameof(firstEventNumbers),
					$"FirstEventNumber: {firstEventNumber}");

				if (lastEventNumber - firstEventNumber + 1 < 0)
					throw new ArgumentOutOfRangeException(nameof(lastEventNumbers),
					$"LastEventNumber {lastEventNumber}, FirstEventNumber {firstEventNumber}.");
			}

			CorrelationId = correlationId;
			Result = OperationResult.Success;
			Message = null;
			FirstEventNumbers = firstEventNumbers;
			LastEventNumbers = lastEventNumbers;
			PreparePosition = preparePosition;
			CommitPosition = commitPosition;
		}

		/// <summary>Failure constructor</summary>
		public WriteEventsCompleted(Guid correlationId, OperationResult result, string message,
			LowAllocReadOnlyMemory<int> failureStreamIndexes = default, LowAllocReadOnlyMemory<long> failureCurrentVersions = default) {
			ArgumentOutOfRangeException.ThrowIfNotEqual(failureStreamIndexes.Length, failureCurrentVersions.Length, nameof(failureStreamIndexes));

			if (result == OperationResult.Success)
				throw new ArgumentException("Invalid constructor used for successful write.", nameof(result));

			CorrelationId = correlationId;
			Result = result;
			Message = message;
			FirstEventNumbers = [];
			LastEventNumbers = [];
			PreparePosition = EventNumber.Invalid;
			FailureStreamIndexes = failureStreamIndexes;
			FailureCurrentVersions = failureCurrentVersions;
		}

		private WriteEventsCompleted(Guid correlationId, OperationResult result, string message,
			LowAllocReadOnlyMemory<long> firstEventNumbers, LowAllocReadOnlyMemory<long> lastEventNumbers, long preparePosition,
			long commitPosition, LowAllocReadOnlyMemory<int> failureStreamIndexes, LowAllocReadOnlyMemory<long> failureCurrentVersions) {
			ArgumentOutOfRangeException.ThrowIfNotEqual(firstEventNumbers.Length, lastEventNumbers.Length, nameof(firstEventNumbers));
			ArgumentOutOfRangeException.ThrowIfNotEqual(failureStreamIndexes.Length, failureCurrentVersions.Length, nameof(failureStreamIndexes));

			CorrelationId = correlationId;
			Result = result;
			Message = message;
			FirstEventNumbers = firstEventNumbers;
			LastEventNumbers = lastEventNumbers;
			PreparePosition = preparePosition;
			CommitPosition = commitPosition;
			FailureStreamIndexes = failureStreamIndexes;
			FailureCurrentVersions = failureCurrentVersions;
		}

		public static WriteEventsCompleted ForSingleStream(Guid correlationId, long firstEventNumber, long lastEventNumber, long preparePosition, long commitPosition) {
			return new WriteEventsCompleted(
				correlationId,
				firstEventNumbers: new(firstEventNumber),
				lastEventNumbers: new(lastEventNumber),
				preparePosition,
				commitPosition);
		}

		public WriteEventsCompleted WithCorrelationId(Guid newCorrId) {
			return new WriteEventsCompleted(newCorrId, Result, Message, FirstEventNumbers, LastEventNumbers,
				PreparePosition, CommitPosition, FailureStreamIndexes, FailureCurrentVersions);
		}

		public override string ToString() {
			return
				"WRITE COMPLETED: " +
				$"CorrelationId: {CorrelationId}, " +
				$"Result: {Result}, " +
				$"Message: {Message}, " +
				$"FirstEventNumbers: {string.Join(", ", FirstEventNumbers.ToArray())}," +
				$"LastEventNumbers: {string.Join(", ", LastEventNumbers.ToArray())}," +
				$"FailureStreamIndexes: {string.Join(", ", FailureStreamIndexes.ToArray())}" +
				$"FailureCurrentVersions: {string.Join(", ", FailureCurrentVersions.ToArray())}";
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class TransactionStart : WriteRequestMessage {
		public readonly string EventStreamId;
		public readonly long ExpectedVersion;

		public TransactionStart(Guid internalCorrId, Guid correlationId, IEnvelope envelope, bool requireLeader,
			string eventStreamId, long expectedVersion, ClaimsPrincipal user,
			IReadOnlyDictionary<string, string> tokens = null)
			: base(internalCorrId, correlationId, envelope, requireLeader, user, tokens, CancellationToken.None) {
			Ensure.NotNullOrEmpty(eventStreamId, "eventStreamId");
			if (expectedVersion < KurrentDB.Core.Data.ExpectedVersion.Any)
				throw new ArgumentOutOfRangeException(nameof(expectedVersion));

			EventStreamId = eventStreamId;
			ExpectedVersion = expectedVersion;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class TransactionStartCompleted : Message {
		public readonly Guid CorrelationId;
		public readonly long TransactionId;
		public readonly OperationResult Result;
		public readonly string Message;

		public TransactionStartCompleted(Guid correlationId, long transactionId, OperationResult result,
			string message) {
			CorrelationId = correlationId;
			TransactionId = transactionId;
			Result = result;
			Message = message;
		}

		public TransactionStartCompleted WithCorrelationId(Guid newCorrId) {
			return new TransactionStartCompleted(newCorrId, TransactionId, Result, Message);
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class TransactionWrite : WriteRequestMessage {
		public readonly long TransactionId;
		public readonly Event[] Events;

		public TransactionWrite(Guid internalCorrId, Guid correlationId, IEnvelope envelope, bool requireLeader,
			long transactionId, Event[] events, ClaimsPrincipal user, IReadOnlyDictionary<string, string> tokens = null)
			: base(internalCorrId, correlationId, envelope, requireLeader, user, tokens, CancellationToken.None) {
			Ensure.Nonnegative(transactionId, "transactionId");
			Ensure.NotNull(events, "events");

			TransactionId = transactionId;
			Events = events;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class TransactionWriteCompleted : Message {
		public readonly Guid CorrelationId;
		public readonly long TransactionId;
		public readonly OperationResult Result;
		public readonly string Message;

		public TransactionWriteCompleted(Guid correlationId, long transactionId, OperationResult result,
			string message) {
			CorrelationId = correlationId;
			TransactionId = transactionId;
			Result = result;
			Message = message;
		}

		public TransactionWriteCompleted WithCorrelationId(Guid newCorrId) {
			return new TransactionWriteCompleted(newCorrId, TransactionId, Result, Message);
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class TransactionCommit : WriteRequestMessage {
		public readonly long TransactionId;

		public TransactionCommit(Guid internalCorrId, Guid correlationId, IEnvelope envelope, bool requireLeader,
			long transactionId, ClaimsPrincipal user, IReadOnlyDictionary<string, string> tokens = null)
			: base(internalCorrId, correlationId, envelope, requireLeader, user, tokens, CancellationToken.None) {
			Ensure.Nonnegative(transactionId, "transactionId");
			TransactionId = transactionId;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class TransactionCommitCompleted : Message {
		public readonly Guid CorrelationId;
		public readonly long TransactionId;
		public readonly OperationResult Result;
		public readonly string Message;
		public readonly long FirstEventNumber;
		public readonly long LastEventNumber;
		public readonly long PreparePosition;
		public readonly long CommitPosition;

		public TransactionCommitCompleted(Guid correlationId, long transactionId, long firstEventNumber,
			long lastEventNumber, long preparePosition, long commitPosition) {
			if (firstEventNumber < -1)
				throw new ArgumentOutOfRangeException("firstEventNumber",
					String.Format("FirstEventNumber: {0}", firstEventNumber));
			if (lastEventNumber - firstEventNumber + 1 < 0)
				throw new ArgumentOutOfRangeException("lastEventNumber",
					String.Format("LastEventNumber {0}, FirstEventNumber {1}.", lastEventNumber, firstEventNumber));
			CorrelationId = correlationId;
			TransactionId = transactionId;
			Result = OperationResult.Success;
			Message = String.Empty;
			FirstEventNumber = firstEventNumber;
			LastEventNumber = lastEventNumber;
			PreparePosition = preparePosition;
			CommitPosition = commitPosition;
		}

		public TransactionCommitCompleted(Guid correlationId, long transactionId, OperationResult result,
			string message) {
			if (result == OperationResult.Success)
				throw new ArgumentException("Invalid constructor used for successful write.", "result");

			CorrelationId = correlationId;
			TransactionId = transactionId;
			Result = result;
			Message = message;
			FirstEventNumber = EventNumber.Invalid;
			LastEventNumber = EventNumber.Invalid;
		}

		private TransactionCommitCompleted(Guid correlationId, long transactionId, OperationResult result,
			string message,
			long firstEventNumber, long lastEventNumber) {
			CorrelationId = correlationId;
			TransactionId = transactionId;
			Result = result;
			Message = message;
			FirstEventNumber = firstEventNumber;
			LastEventNumber = lastEventNumber;
		}

		public TransactionCommitCompleted WithCorrelationId(Guid newCorrId) {
			return new TransactionCommitCompleted(newCorrId, TransactionId, Result, Message, FirstEventNumber,
				LastEventNumber);
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class DeleteStream : WriteRequestMessage {
		public readonly string EventStreamId;
		public readonly long ExpectedVersion;
		public readonly bool HardDelete;

		public DeleteStream(Guid internalCorrId, Guid correlationId, IEnvelope envelope, bool requireLeader,
			string eventStreamId, long expectedVersion, bool hardDelete, ClaimsPrincipal user,
			IReadOnlyDictionary<string, string> tokens = null, CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, requireLeader, user, tokens, cancellationToken) {
			Ensure.NotNullOrEmpty(eventStreamId, "eventStreamId");
			EventStreamId = eventStreamId;
			ExpectedVersion = expectedVersion switch {
				KurrentDB.Core.Data.ExpectedVersion.Invalid => throw new ArgumentOutOfRangeException(nameof(expectedVersion)),
				< KurrentDB.Core.Data.ExpectedVersion.StreamExists => throw new ArgumentOutOfRangeException(nameof(expectedVersion)),
				_ => expectedVersion
			};
			HardDelete = hardDelete;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadLogEvents(
		Guid internalCorrId,
		Guid correlationId,
		IEnvelope envelope,
		long[] logPositions,
		ClaimsPrincipal user,
		DateTime? expires,
		CancellationToken cancellationToken = default)
		: ReadRequestMessage(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
		public long[] LogPositions = logPositions;
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadLogEventsCompleted(Guid correlationId, ReadEventResult result, ResolvedEvent[] records, string error)
		: ReadResponseMessage {
		public readonly Guid CorrelationId = correlationId;
		public readonly ReadEventResult Result = result;
		public readonly ResolvedEvent[] Records = records;
		public readonly string Error = error;
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class DeleteStreamCompleted : Message {
		public readonly Guid CorrelationId;
		public readonly OperationResult Result;
		public readonly string Message;
		public readonly long PreparePosition;
		public readonly long CommitPosition;
		public readonly long CurrentVersion;

		public DeleteStreamCompleted(Guid correlationId, OperationResult result, string message,
			long currentVersion, long preparePosition, long commitPosition) {
			CorrelationId = correlationId;
			Result = result;
			Message = message;
			CurrentVersion = currentVersion;
			PreparePosition = preparePosition;
			CommitPosition = commitPosition;
		}

		public DeleteStreamCompleted(Guid correlationId, OperationResult result, string message,
			long currentVersion = -1L) : this(correlationId, result, message, currentVersion, -1, -1) {
		}

		public DeleteStreamCompleted WithCorrelationId(Guid newCorrId) {
			return new DeleteStreamCompleted(newCorrId, Result, Message, CurrentVersion, PreparePosition,
				CommitPosition);
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadEvent : ReadRequestMessage {
		public readonly string EventStreamId;
		public readonly long EventNumber;
		public readonly bool ResolveLinkTos;
		public readonly bool RequireLeader;

		public ReadEvent(Guid internalCorrId, Guid correlationId, IEnvelope envelope, string eventStreamId,
			long eventNumber,
			bool resolveLinkTos, bool requireLeader, ClaimsPrincipal user, DateTime? expires = null,
			CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
			Ensure.NotNullOrEmpty(eventStreamId, "eventStreamId");
			if (eventNumber < -1)
				throw new ArgumentOutOfRangeException(nameof(eventNumber));

			EventStreamId = eventStreamId;
			EventNumber = eventNumber;
			ResolveLinkTos = resolveLinkTos;
			RequireLeader = requireLeader;
		}

		public override string ToString() =>
			$"{base.ToString()}, " +
			$"EventStreamId: {EventStreamId}, " +
			$"EventNumber: {EventNumber}, " +
			$"ResolveLinkTos: {ResolveLinkTos}, " +
			$"RequireLeader: {RequireLeader}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadEventCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string EventStreamId;
		public readonly ReadEventResult Result;
		public readonly ResolvedEvent Record;
		public readonly StreamMetadata StreamMetadata;
		public readonly bool IsCachePublic;
		public readonly string Error;

		public ReadEventCompleted(Guid correlationId, string eventStreamId, ReadEventResult result,
			ResolvedEvent record, StreamMetadata streamMetadata, bool isCachePublic, string error) {
			Ensure.NotNullOrEmpty(eventStreamId, "eventStreamId");
			//if (result == ReadEventResult.Success)
			//    Ensure.NotNull(record.Event, "record.Event");

			CorrelationId = correlationId;
			EventStreamId = eventStreamId;
			Result = result;
			Record = record;
			StreamMetadata = streamMetadata;
			IsCachePublic = isCachePublic;
			Error = error;
		}
	}


	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadStreamEventsForward : ReadRequestMessage {
		public readonly string EventStreamId;
		public readonly long FromEventNumber;
		public readonly int MaxCount;
		public readonly bool ResolveLinkTos;
		public readonly bool RequireLeader;

		public readonly long? ValidationStreamVersion;
		public readonly TimeSpan? LongPollTimeout;
		public readonly bool ReplyOnExpired;

		public ReadStreamEventsForward(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string eventStreamId, long fromEventNumber, int maxCount, bool resolveLinkTos,
			bool requireLeader, long? validationStreamVersion, ClaimsPrincipal user,
			bool replyOnExpired,
			TimeSpan? longPollTimeout = null, DateTime? expires = null,
			CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
			Ensure.NotNullOrEmpty(eventStreamId, "eventStreamId");
			if (fromEventNumber < -1)
				throw new ArgumentOutOfRangeException(nameof(fromEventNumber));

			EventStreamId = eventStreamId;
			FromEventNumber = fromEventNumber;
			MaxCount = maxCount;
			ResolveLinkTos = resolveLinkTos;
			RequireLeader = requireLeader;
			ValidationStreamVersion = validationStreamVersion;
			LongPollTimeout = longPollTimeout;
			ReplyOnExpired = replyOnExpired;
		}

		public override string ToString() =>
			$"{base.ToString()}, " +
			$"EventStreamId: {EventStreamId}, " +
			$"FromEventNumber: {FromEventNumber}, " +
			$"MaxCount: {MaxCount}, " +
			$"ResolveLinkTos: {ResolveLinkTos}, " +
			$"RequireLeader: {RequireLeader}, " +
			$"ValidationStreamVersion: {ValidationStreamVersion}, " +
			$"LongPollTimeout: {LongPollTimeout}, " +
			$"ReplyOnExpired: {ReplyOnExpired}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadStreamEventsForwardCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string EventStreamId;
		public readonly long FromEventNumber;
		public readonly int MaxCount;

		public readonly ReadStreamResult Result;
		public readonly IReadOnlyList<ResolvedEvent> Events;
		public readonly StreamMetadata StreamMetadata;
		public readonly bool IsCachePublic;
		public readonly string Error;
		public readonly long NextEventNumber;
		public readonly long LastEventNumber;
		public readonly bool IsEndOfStream;
		public readonly long TfLastCommitPosition;

		public ReadStreamEventsForwardCompleted(Guid correlationId, string eventStreamId, long fromEventNumber,
			int maxCount,
			ReadStreamResult result, IReadOnlyList<ResolvedEvent> events,
			StreamMetadata streamMetadata, bool isCachePublic,
			string error, long nextEventNumber, long lastEventNumber, bool isEndOfStream,
			long tfLastCommitPosition) {
			Ensure.NotNull(events, "events");

			if (result != ReadStreamResult.Success && result != ReadStreamResult.Expired) {
				Ensure.Equal(nextEventNumber, -1, "nextEventNumber");
				Ensure.Equal(isEndOfStream, true, "isEndOfStream");
			}

			CorrelationId = correlationId;
			EventStreamId = eventStreamId;
			FromEventNumber = fromEventNumber;
			MaxCount = maxCount;

			Result = result;
			Events = events;
			StreamMetadata = streamMetadata;
			IsCachePublic = isCachePublic;
			Error = error;
			NextEventNumber = nextEventNumber;
			LastEventNumber = lastEventNumber;
			IsEndOfStream = isEndOfStream;
			TfLastCommitPosition = tfLastCommitPosition;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadStreamEventsBackward : ReadRequestMessage {
		public readonly string EventStreamId;
		public readonly long FromEventNumber;
		public readonly int MaxCount;
		public readonly bool ResolveLinkTos;
		public readonly bool RequireLeader;

		public readonly long? ValidationStreamVersion;

		public ReadStreamEventsBackward(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string eventStreamId, long fromEventNumber, int maxCount, bool resolveLinkTos,
			bool requireLeader, long? validationStreamVersion, ClaimsPrincipal user, DateTime? expires = null,
			CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
			Ensure.NotNullOrEmpty(eventStreamId, "eventStreamId");
			if (fromEventNumber < -1)
				throw new ArgumentOutOfRangeException(nameof(fromEventNumber));

			EventStreamId = eventStreamId;
			FromEventNumber = fromEventNumber;
			MaxCount = maxCount;
			ResolveLinkTos = resolveLinkTos;
			RequireLeader = requireLeader;
			ValidationStreamVersion = validationStreamVersion;
		}

		public override string ToString() =>
			$"{base.ToString()}, " +
			$"EventStreamId: {EventStreamId}, " +
			$"FromEventNumber: {FromEventNumber}, " +
			$"MaxCount: {MaxCount}, " +
			$"ResolveLinkTos: {ResolveLinkTos}, " +
			$"RequireLeader: {RequireLeader}, " +
			$"ValidationStreamVersion: {ValidationStreamVersion}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadStreamEventsBackwardCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string EventStreamId;
		public readonly long FromEventNumber;
		public readonly int MaxCount;

		public readonly ReadStreamResult Result;
		public readonly IReadOnlyList<ResolvedEvent> Events;
		public readonly StreamMetadata StreamMetadata;
		public readonly bool IsCachePublic;
		public readonly string Error;
		public readonly long NextEventNumber;
		public readonly long LastEventNumber;
		public readonly bool IsEndOfStream;
		public readonly long TfLastCommitPosition;

		public ReadStreamEventsBackwardCompleted(Guid correlationId,
			string eventStreamId,
			long fromEventNumber,
			int maxCount,
			ReadStreamResult result,
			IReadOnlyList<ResolvedEvent> events,
			StreamMetadata streamMetadata,
			bool isCachePublic,
			string error,
			long nextEventNumber,
			long lastEventNumber,
			bool isEndOfStream,
			long tfLastCommitPosition) {
			Ensure.NotNull(events, "events");

			if (result != ReadStreamResult.Success) {
				Ensure.Equal(nextEventNumber, -1, "nextEventNumber");
				Ensure.Equal(isEndOfStream, true, "isEndOfStream");
			}

			CorrelationId = correlationId;
			EventStreamId = eventStreamId;
			FromEventNumber = fromEventNumber;
			MaxCount = maxCount;

			Result = result;
			Events = events;
			StreamMetadata = streamMetadata;
			IsCachePublic = isCachePublic;
			Error = error;
			NextEventNumber = nextEventNumber;
			LastEventNumber = lastEventNumber;
			IsEndOfStream = isEndOfStream;
			TfLastCommitPosition = tfLastCommitPosition;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadAllEventsForward : ReadRequestMessage {
		public readonly long CommitPosition;
		public readonly long PreparePosition;
		public readonly int MaxCount;
		public readonly bool ResolveLinkTos;
		public readonly bool RequireLeader;

		public readonly long? ValidationTfLastCommitPosition;
		public readonly TimeSpan? LongPollTimeout;
		public readonly bool ReplyOnExpired;

		public ReadAllEventsForward(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			long commitPosition, long preparePosition, int maxCount, bool resolveLinkTos,
			bool requireLeader, long? validationTfLastCommitPosition, ClaimsPrincipal user,
			bool replyOnExpired,
			TimeSpan? longPollTimeout = null, DateTime? expires = null,
			CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
			CommitPosition = commitPosition;
			PreparePosition = preparePosition;
			MaxCount = maxCount;
			ResolveLinkTos = resolveLinkTos;
			RequireLeader = requireLeader;
			ValidationTfLastCommitPosition = validationTfLastCommitPosition;
			LongPollTimeout = longPollTimeout;
			ReplyOnExpired = replyOnExpired;
		}

		public override string ToString() =>
			$"{base.ToString()}, " +
			$"PreparePosition: {PreparePosition}, " +
			$"CommitPosition: {CommitPosition}, " +
			$"MaxCount: {MaxCount}, " +
			$"ResolveLinkTos: {ResolveLinkTos}, " +
			$"RequireLeader: {RequireLeader}, " +
			$"ValidationTfLastCommitPosition: {ValidationTfLastCommitPosition}, " +
			$"LongPollTimeout: {LongPollTimeout}, " +
			$"ReplyOnExpired: {ReplyOnExpired}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadAllEventsForwardCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;

		public readonly ReadAllResult Result;
		public readonly string Error;

		public readonly IReadOnlyList<ResolvedEvent> Events;
		public readonly StreamMetadata StreamMetadata;
		public readonly bool IsCachePublic;
		public readonly int MaxCount;
		public readonly TFPos CurrentPos;
		public readonly TFPos NextPos;
		public readonly TFPos PrevPos;
		public readonly long TfLastCommitPosition;

		public bool IsEndOfStream {
			get { return Events == null || Events.Count < MaxCount; }
		}

		public ReadAllEventsForwardCompleted(Guid correlationId, ReadAllResult result, string error,
			IReadOnlyList<ResolvedEvent> events,
			StreamMetadata streamMetadata, bool isCachePublic, int maxCount,
			TFPos currentPos, TFPos nextPos, TFPos prevPos, long tfLastCommitPosition) {
			Ensure.NotNull(events, "events");

			CorrelationId = correlationId;
			Result = result;
			Error = error;
			Events = events;
			StreamMetadata = streamMetadata;
			IsCachePublic = isCachePublic;
			MaxCount = maxCount;
			CurrentPos = currentPos;
			NextPos = nextPos;
			PrevPos = prevPos;
			TfLastCommitPosition = tfLastCommitPosition;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadAllEventsBackward : ReadRequestMessage {
		public readonly long CommitPosition;
		public readonly long PreparePosition;
		public readonly int MaxCount;
		public readonly bool ResolveLinkTos;
		public readonly bool RequireLeader;

		public readonly long? ValidationTfLastCommitPosition;

		public ReadAllEventsBackward(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			long commitPosition, long preparePosition, int maxCount, bool resolveLinkTos,
			bool requireLeader, long? validationTfLastCommitPosition, ClaimsPrincipal user,
			DateTime? expires = null,
			CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
			CommitPosition = commitPosition;
			PreparePosition = preparePosition;
			MaxCount = maxCount;
			ResolveLinkTos = resolveLinkTos;
			RequireLeader = requireLeader;
			ValidationTfLastCommitPosition = validationTfLastCommitPosition;
		}

		public override string ToString() =>
			$"{base.ToString()}, " +
			$"PreparePosition: {PreparePosition}, " +
			$"CommitPosition: {CommitPosition}, " +
			$"MaxCount: {MaxCount}, " +
			$"ResolveLinkTos: {ResolveLinkTos}, " +
			$"RequireLeader: {RequireLeader}, " +
			$"ValidationTfLastCommitPosition: {ValidationTfLastCommitPosition}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadAllEventsBackwardCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;

		public readonly ReadAllResult Result;
		public readonly string Error;

		public readonly IReadOnlyList<ResolvedEvent> Events;
		public readonly StreamMetadata StreamMetadata;
		public readonly bool IsCachePublic;
		public readonly int MaxCount;
		public readonly TFPos CurrentPos;
		public readonly TFPos NextPos;
		public readonly TFPos PrevPos;
		public readonly long TfLastCommitPosition;

		public bool IsEndOfStream {
			get { return Events == null || Events.Count < MaxCount; }
		}

		public ReadAllEventsBackwardCompleted(Guid correlationId, ReadAllResult result, string error,
			IReadOnlyList<ResolvedEvent> events,
			StreamMetadata streamMetadata, bool isCachePublic, int maxCount,
			TFPos currentPos, TFPos nextPos, TFPos prevPos, long tfLastCommitPosition) {
			Ensure.NotNull(events, "events");

			CorrelationId = correlationId;
			Result = result;
			Error = error;
			Events = events;
			StreamMetadata = streamMetadata;
			IsCachePublic = isCachePublic;
			MaxCount = maxCount;
			CurrentPos = currentPos;
			NextPos = nextPos;
			PrevPos = prevPos;
			TfLastCommitPosition = tfLastCommitPosition;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class FilteredReadAllEventsForward : ReadRequestMessage {
		public readonly long CommitPosition;
		public readonly long PreparePosition;
		public readonly int MaxCount;
		public readonly bool ResolveLinkTos;
		public readonly bool RequireLeader;
		public readonly int MaxSearchWindow;
		public readonly IEventFilter EventFilter;
		public readonly bool ReplyOnExpired;

		public readonly long? ValidationTfLastCommitPosition;
		public readonly TimeSpan? LongPollTimeout;

		public FilteredReadAllEventsForward(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			long commitPosition, long preparePosition, int maxCount, bool resolveLinkTos, bool requireLeader,
			int maxSearchWindow, long? validationTfLastCommitPosition, IEventFilter eventFilter, ClaimsPrincipal user,
			bool replyOnExpired,
			TimeSpan? longPollTimeout = null, DateTime? expires = null,
			CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
			CommitPosition = commitPosition;
			PreparePosition = preparePosition;
			MaxCount = maxCount;
			ResolveLinkTos = resolveLinkTos;
			RequireLeader = requireLeader;
			ValidationTfLastCommitPosition = validationTfLastCommitPosition;
			LongPollTimeout = longPollTimeout;
			MaxSearchWindow = maxSearchWindow;
			EventFilter = eventFilter;
			ReplyOnExpired = replyOnExpired;
		}

		public override string ToString() =>
			$"{base.ToString()}, " +
			$"PreparePosition: {PreparePosition}, " +
			$"CommitPosition: {CommitPosition}, " +
			$"MaxCount: {MaxCount}, " +
			$"ResolveLinkTos: {ResolveLinkTos}, " +
			$"RequireLeader: {RequireLeader}, " +
			$"ValidationTfLastCommitPosition: {ValidationTfLastCommitPosition}, " +
			$"LongPollTimeout: {LongPollTimeout}, " +
			$"MaxSearchWindow: {MaxSearchWindow}, " +
			$"EventFilter: {{ {EventFilter} }}, " +
			$"ReplyOnExpired: {ReplyOnExpired}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class FilteredReadAllEventsForwardCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;

		public readonly FilteredReadAllResult Result;
		public readonly string Error;

		public readonly IReadOnlyList<ResolvedEvent> Events;
		public readonly StreamMetadata StreamMetadata;
		public readonly bool IsCachePublic;
		public readonly int MaxCount;
		public readonly TFPos CurrentPos;
		public readonly TFPos NextPos;
		public readonly TFPos PrevPos;
		public readonly long TfLastCommitPosition;
		public readonly bool IsEndOfStream;
		public readonly long ConsideredEventsCount;

		public FilteredReadAllEventsForwardCompleted(Guid correlationId, FilteredReadAllResult result, string error,
			IReadOnlyList<ResolvedEvent> events,
			StreamMetadata streamMetadata, bool isCachePublic, int maxCount,
			TFPos currentPos, TFPos nextPos, TFPos prevPos, long tfLastCommitPosition,
			bool isEndOfStream, long consideredEventsCount) {
			Ensure.NotNull(events, "events");

			CorrelationId = correlationId;
			Result = result;
			Error = error;
			Events = events;
			StreamMetadata = streamMetadata;
			IsCachePublic = isCachePublic;
			MaxCount = maxCount;
			CurrentPos = currentPos;
			NextPos = nextPos;
			PrevPos = prevPos;
			TfLastCommitPosition = tfLastCommitPosition;
			IsEndOfStream = isEndOfStream;
			ConsideredEventsCount = consideredEventsCount;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class FilteredReadAllEventsBackward : ReadRequestMessage {
		public readonly long CommitPosition;
		public readonly long PreparePosition;
		public readonly int MaxCount;
		public readonly bool ResolveLinkTos;
		public readonly bool RequireLeader;
		public readonly int MaxSearchWindow;
		public readonly IEventFilter EventFilter;

		public readonly long? ValidationTfLastCommitPosition;
		public readonly TimeSpan? LongPollTimeout;

		public FilteredReadAllEventsBackward(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			long commitPosition, long preparePosition, int maxCount, bool resolveLinkTos, bool requireLeader,
			int maxSearchWindow, long? validationTfLastCommitPosition, IEventFilter eventFilter, ClaimsPrincipal user,
			TimeSpan? longPollTimeout = null, DateTime? expires = null,
			CancellationToken cancellationToken = default)
			: base(internalCorrId, correlationId, envelope, user, expires, cancellationToken) {
			CommitPosition = commitPosition;
			PreparePosition = preparePosition;
			MaxCount = maxCount;
			ResolveLinkTos = resolveLinkTos;
			RequireLeader = requireLeader;
			ValidationTfLastCommitPosition = validationTfLastCommitPosition;
			LongPollTimeout = longPollTimeout;
			MaxSearchWindow = maxSearchWindow;
			EventFilter = eventFilter;
		}

		public override string ToString() =>
			$"{base.ToString()}, " +
			$"PreparePosition: {PreparePosition}, " +
			$"CommitPosition: {CommitPosition}, " +
			$"MaxCount: {MaxCount}, " +
			$"ResolveLinkTos: {ResolveLinkTos}, " +
			$"RequireLeader: {RequireLeader}, " +
			$"MaxSearchWindow: {MaxSearchWindow}, " +
			$"EventFilter: {{ {EventFilter} }}, " +
			$"LongPollTimeout: {LongPollTimeout}, " +
			$"ValidationTfLastCommitPosition: {ValidationTfLastCommitPosition}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class FilteredReadAllEventsBackwardCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;

		public readonly FilteredReadAllResult Result;
		public readonly string Error;

		public readonly IReadOnlyList<ResolvedEvent> Events;
		public readonly StreamMetadata StreamMetadata;
		public readonly bool IsCachePublic;
		public readonly int MaxCount;
		public readonly TFPos CurrentPos;
		public readonly TFPos NextPos;
		public readonly TFPos PrevPos;
		public readonly long TfLastCommitPosition;
		public readonly bool IsEndOfStream;

		public FilteredReadAllEventsBackwardCompleted(Guid correlationId, FilteredReadAllResult result, string error,
			IReadOnlyList<ResolvedEvent> events,
			StreamMetadata streamMetadata, bool isCachePublic, int maxCount,
			TFPos currentPos, TFPos nextPos, TFPos prevPos, long tfLastCommitPosition,
			bool isEndOfStream) {
			Ensure.NotNull(events, "events");

			CorrelationId = correlationId;
			Result = result;
			Error = error;
			Events = events;
			StreamMetadata = streamMetadata;
			IsCachePublic = isCachePublic;
			MaxCount = maxCount;
			CurrentPos = currentPos;
			NextPos = nextPos;
			PrevPos = prevPos;
			TfLastCommitPosition = tfLastCommitPosition;
			IsEndOfStream = isEndOfStream;
		}
	}

	//Persistent subscriptions
	[DerivedMessage(CoreMessage.Client)]
	public partial class ConnectToPersistentSubscriptionToStream : ReadRequestMessage {
		public readonly Guid ConnectionId;
		public readonly string ConnectionName;
		public readonly string GroupName;
		public readonly string EventStreamId;
		public readonly int AllowedInFlightMessages;
		public readonly string From;

		public ConnectToPersistentSubscriptionToStream(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			Guid connectionId, string connectionName, string groupName, string eventStreamId,
			int allowedInFlightMessages, string from, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			Ensure.NotEmptyGuid(connectionId, "connectionId");
			Ensure.NotNullOrEmpty(groupName, "subscriptionId");
			Ensure.Nonnegative(allowedInFlightMessages, "AllowedInFlightMessages");
			GroupName = groupName;
			ConnectionId = connectionId;
			ConnectionName = connectionName;
			AllowedInFlightMessages = allowedInFlightMessages;
			EventStreamId = eventStreamId;
			From = from;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ConnectToPersistentSubscriptionToAll : ReadRequestMessage {
		public readonly Guid ConnectionId;
		public readonly string ConnectionName;
		public readonly string GroupName;
		public readonly int AllowedInFlightMessages;
		public readonly string From;

		public ConnectToPersistentSubscriptionToAll(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			Guid connectionId, string connectionName, string groupName,
			int allowedInFlightMessages, string from, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			Ensure.NotEmptyGuid(connectionId, "connectionId");
			Ensure.NotNullOrEmpty(groupName, "groupName");
			Ensure.Nonnegative(allowedInFlightMessages, "AllowedInFlightMessages");
			GroupName = groupName;
			ConnectionId = connectionId;
			ConnectionName = connectionName;
			AllowedInFlightMessages = allowedInFlightMessages;
			From = from;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class CreatePersistentSubscriptionToStream : ReadRequestMessage {
		public readonly long StartFrom;
		public readonly int MessageTimeoutMilliseconds;
		public readonly bool RecordStatistics;

		public readonly bool ResolveLinkTos;
		public readonly int MaxRetryCount;
		public readonly int BufferSize;
		public readonly int LiveBufferSize;
		public readonly int ReadBatchSize;

		public readonly string GroupName;
		public readonly string EventStreamId;
		public readonly int MaxSubscriberCount;
		public readonly string NamedConsumerStrategy;
		public readonly int MaxCheckPointCount;
		public readonly int MinCheckPointCount;
		public readonly int CheckPointAfterMilliseconds;

		public CreatePersistentSubscriptionToStream(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string eventStreamId, string groupName, bool resolveLinkTos, long startFrom,
			int messageTimeoutMilliseconds, bool recordStatistics, int maxRetryCount, int bufferSize,
			int liveBufferSize, int readbatchSize,
			int checkPointAfterMilliseconds, int minCheckPointCount, int maxCheckPointCount,
			int maxSubscriberCount, string namedConsumerStrategy, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			ResolveLinkTos = resolveLinkTos;
			EventStreamId = eventStreamId;
			GroupName = groupName;
			StartFrom = startFrom;
			MessageTimeoutMilliseconds = messageTimeoutMilliseconds;
			RecordStatistics = recordStatistics;
			MaxRetryCount = maxRetryCount;
			BufferSize = bufferSize;
			LiveBufferSize = liveBufferSize;
			ReadBatchSize = readbatchSize;
			MaxCheckPointCount = maxCheckPointCount;
			MinCheckPointCount = minCheckPointCount;
			CheckPointAfterMilliseconds = checkPointAfterMilliseconds;
			MaxSubscriberCount = maxSubscriberCount;
			NamedConsumerStrategy = namedConsumerStrategy;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class CreatePersistentSubscriptionToStreamCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string Reason;
		public readonly CreatePersistentSubscriptionToStreamResult Result;

		public CreatePersistentSubscriptionToStreamCompleted(Guid correlationId, CreatePersistentSubscriptionToStreamResult result,
			string reason) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Result = result;
			Reason = reason;
		}

		public enum CreatePersistentSubscriptionToStreamResult {
			Success = 0,
			AlreadyExists = 1,
			Fail = 2,
			AccessDenied = 3
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class CreatePersistentSubscriptionToAll : ReadRequestMessage {
		public readonly IEventFilter EventFilter;

		public readonly TFPos StartFrom;
		public readonly int MessageTimeoutMilliseconds;
		public readonly bool RecordStatistics;

		public readonly bool ResolveLinkTos;
		public readonly int MaxRetryCount;
		public readonly int BufferSize;
		public readonly int LiveBufferSize;
		public readonly int ReadBatchSize;

		public readonly string GroupName;
		public readonly int MaxSubscriberCount;
		public readonly string NamedConsumerStrategy;
		public readonly int MaxCheckPointCount;
		public readonly int MinCheckPointCount;
		public readonly int CheckPointAfterMilliseconds;

		public CreatePersistentSubscriptionToAll(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string groupName, IEventFilter eventFilter, bool resolveLinkTos, TFPos startFrom,
			int messageTimeoutMilliseconds, bool recordStatistics, int maxRetryCount, int bufferSize,
			int liveBufferSize, int readbatchSize,
			int checkPointAfterMilliseconds, int minCheckPointCount, int maxCheckPointCount,
			int maxSubscriberCount, string namedConsumerStrategy, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			ResolveLinkTos = resolveLinkTos;
			GroupName = groupName;
			EventFilter = eventFilter;
			StartFrom = startFrom;
			MessageTimeoutMilliseconds = messageTimeoutMilliseconds;
			RecordStatistics = recordStatistics;
			MaxRetryCount = maxRetryCount;
			BufferSize = bufferSize;
			LiveBufferSize = liveBufferSize;
			ReadBatchSize = readbatchSize;
			MaxCheckPointCount = maxCheckPointCount;
			MinCheckPointCount = minCheckPointCount;
			CheckPointAfterMilliseconds = checkPointAfterMilliseconds;
			MaxSubscriberCount = maxSubscriberCount;
			NamedConsumerStrategy = namedConsumerStrategy;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class CreatePersistentSubscriptionToAllCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string Reason;
		public readonly CreatePersistentSubscriptionToAllResult Result;

		public CreatePersistentSubscriptionToAllCompleted(Guid correlationId, CreatePersistentSubscriptionToAllResult result,
			string reason) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Result = result;
			Reason = reason;
		}

		public enum CreatePersistentSubscriptionToAllResult {
			Success = 0,
			AlreadyExists = 1,
			Fail = 2,
			AccessDenied = 3
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class UpdatePersistentSubscriptionToStream : ReadRequestMessage {
		public readonly long StartFrom;
		public readonly int MessageTimeoutMilliseconds;
		public readonly bool RecordStatistics;

		public readonly bool ResolveLinkTos;
		public readonly int MaxRetryCount;
		public readonly int BufferSize;
		public readonly int LiveBufferSize;
		public readonly int ReadBatchSize;

		public readonly string GroupName;
		public readonly string EventStreamId;
		public readonly int MaxSubscriberCount;

		public readonly int MaxCheckPointCount;
		public readonly int MinCheckPointCount;
		public readonly int CheckPointAfterMilliseconds;
		public readonly string NamedConsumerStrategy;

		public UpdatePersistentSubscriptionToStream(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string eventStreamId, string groupName, bool resolveLinkTos, long startFrom,
			int messageTimeoutMilliseconds, bool recordStatistics, int maxRetryCount, int bufferSize,
			int liveBufferSize, int readbatchSize, int checkPointAfterMilliseconds, int minCheckPointCount,
			int maxCheckPointCount, int maxSubscriberCount, string namedConsumerStrategy, ClaimsPrincipal user,
			DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			ResolveLinkTos = resolveLinkTos;
			EventStreamId = eventStreamId;
			GroupName = groupName;
			StartFrom = startFrom;
			MessageTimeoutMilliseconds = messageTimeoutMilliseconds;
			RecordStatistics = recordStatistics;
			MaxRetryCount = maxRetryCount;
			BufferSize = bufferSize;
			LiveBufferSize = liveBufferSize;
			ReadBatchSize = readbatchSize;
			MaxCheckPointCount = maxCheckPointCount;
			MinCheckPointCount = minCheckPointCount;
			CheckPointAfterMilliseconds = checkPointAfterMilliseconds;
			MaxSubscriberCount = maxSubscriberCount;
			NamedConsumerStrategy = namedConsumerStrategy;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class UpdatePersistentSubscriptionToStreamCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string Reason;
		public readonly UpdatePersistentSubscriptionToStreamResult Result;

		public UpdatePersistentSubscriptionToStreamCompleted(Guid correlationId, UpdatePersistentSubscriptionToStreamResult result,
			string reason) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Result = result;
			Reason = reason;
		}

		public enum UpdatePersistentSubscriptionToStreamResult {
			Success = 0,
			DoesNotExist = 1,
			Fail = 2,
			AccessDenied = 3
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class UpdatePersistentSubscriptionToAll : ReadRequestMessage {
		public readonly TFPos StartFrom;
		public readonly int MessageTimeoutMilliseconds;
		public readonly bool RecordStatistics;

		public readonly bool ResolveLinkTos;
		public readonly int MaxRetryCount;
		public readonly int BufferSize;
		public readonly int LiveBufferSize;
		public readonly int ReadBatchSize;

		public readonly string GroupName;
		public readonly int MaxSubscriberCount;

		public readonly int MaxCheckPointCount;
		public readonly int MinCheckPointCount;
		public readonly int CheckPointAfterMilliseconds;
		public readonly string NamedConsumerStrategy;

		public UpdatePersistentSubscriptionToAll(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string groupName, bool resolveLinkTos, TFPos startFrom,
			int messageTimeoutMilliseconds, bool recordStatistics, int maxRetryCount, int bufferSize,
			int liveBufferSize, int readbatchSize, int checkPointAfterMilliseconds, int minCheckPointCount,
			int maxCheckPointCount, int maxSubscriberCount, string namedConsumerStrategy, ClaimsPrincipal user,
			DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			ResolveLinkTos = resolveLinkTos;
			GroupName = groupName;
			StartFrom = startFrom;
			MessageTimeoutMilliseconds = messageTimeoutMilliseconds;
			RecordStatistics = recordStatistics;
			MaxRetryCount = maxRetryCount;
			BufferSize = bufferSize;
			LiveBufferSize = liveBufferSize;
			ReadBatchSize = readbatchSize;
			MaxCheckPointCount = maxCheckPointCount;
			MinCheckPointCount = minCheckPointCount;
			CheckPointAfterMilliseconds = checkPointAfterMilliseconds;
			MaxSubscriberCount = maxSubscriberCount;
			NamedConsumerStrategy = namedConsumerStrategy;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class UpdatePersistentSubscriptionToAllCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string Reason;
		public readonly UpdatePersistentSubscriptionToAllResult Result;

		public UpdatePersistentSubscriptionToAllCompleted(Guid correlationId, UpdatePersistentSubscriptionToAllResult result,
			string reason) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Result = result;
			Reason = reason;
		}

		public enum UpdatePersistentSubscriptionToAllResult {
			Success = 0,
			DoesNotExist = 1,
			Fail = 2,
			AccessDenied = 3
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadNextNPersistentMessages : ReadRequestMessage {
		public readonly string GroupName;
		public readonly string EventStreamId;
		public readonly int Count;

		public ReadNextNPersistentMessages(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string eventStreamId, string groupName, int count, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			GroupName = groupName;
			EventStreamId = eventStreamId;
			Count = count;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReadNextNPersistentMessagesCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string Reason;
		public readonly ReadNextNPersistentMessagesResult Result;
		public readonly (ResolvedEvent ResolvedEvent, int RetryCount)[] Events;

		public ReadNextNPersistentMessagesCompleted(Guid correlationId, ReadNextNPersistentMessagesResult result,
			string reason, (ResolvedEvent ResolvedEvent, int RetryCount)[] events) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Result = result;
			Reason = reason;
			Events = events;
		}

		public enum ReadNextNPersistentMessagesResult {
			Success = 0,
			DoesNotExist = 1,
			Fail = 2,
			AccessDenied = 3
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class DeletePersistentSubscriptionToStream : ReadRequestMessage {
		public readonly string GroupName;
		public readonly string EventStreamId;

		public DeletePersistentSubscriptionToStream(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string eventStreamId, string groupName, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			GroupName = groupName;
			EventStreamId = eventStreamId;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class DeletePersistentSubscriptionToStreamCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string Reason;
		public readonly DeletePersistentSubscriptionToStreamResult Result;

		public DeletePersistentSubscriptionToStreamCompleted(Guid correlationId, DeletePersistentSubscriptionToStreamResult result,
			string reason) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Result = result;
			Reason = reason;
		}

		public enum DeletePersistentSubscriptionToStreamResult {
			Success = 0,
			DoesNotExist = 1,
			Fail = 2,
			AccessDenied = 3
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class DeletePersistentSubscriptionToAll : ReadRequestMessage {
		public readonly string GroupName;

		public DeletePersistentSubscriptionToAll(Guid internalCorrId, Guid correlationId, IEnvelope envelope
			, string groupName, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			GroupName = groupName;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class DeletePersistentSubscriptionToAllCompleted : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string Reason;
		public readonly DeletePersistentSubscriptionToAllResult Result;

		public DeletePersistentSubscriptionToAllCompleted(Guid correlationId, DeletePersistentSubscriptionToAllResult result,
			string reason) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Result = result;
			Reason = reason;
		}

		public enum DeletePersistentSubscriptionToAllResult {
			Success = 0,
			DoesNotExist = 1,
			Fail = 2,
			AccessDenied = 3
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class PersistentSubscriptionAckEvents : ReadRequestMessage {
		public readonly string SubscriptionId;
		public readonly Guid[] ProcessedEventIds;

		public PersistentSubscriptionAckEvents(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string subscriptionId, Guid[] processedEventIds, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			Ensure.NotNullOrEmpty(subscriptionId, "subscriptionId");
			Ensure.NotNull(processedEventIds, "processedEventIds");

			SubscriptionId = subscriptionId;
			ProcessedEventIds = processedEventIds;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class PersistentSubscriptionNackEvents : ReadRequestMessage {
		public readonly string SubscriptionId;
		public readonly Guid[] ProcessedEventIds;
		public readonly string Message;
		public readonly NakAction Action;

		public PersistentSubscriptionNackEvents(Guid internalCorrId,
			Guid correlationId,
			IEnvelope envelope,
			string subscriptionId,
			string message,
			NakAction action,
			Guid[] processedEventIds,
			ClaimsPrincipal user,
			DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			SubscriptionId = subscriptionId;
			ProcessedEventIds = processedEventIds;
			Message = message;
			Action = action;
		}

		public enum NakAction {
			Unknown = 0,
			Park = 1,
			Retry = 2,
			Skip = 3,
			Stop = 4
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class PersistentSubscriptionNakEvents : ReadRequestMessage {
		public readonly string SubscriptionId;
		public readonly Guid[] ProcessedEventIds;

		public PersistentSubscriptionNakEvents(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string subscriptionId, Guid[] processedEventIds, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			Ensure.NotNullOrEmpty(subscriptionId, "subscriptionId");
			Ensure.NotNull(processedEventIds, "processedEventIds");

			SubscriptionId = subscriptionId;
			ProcessedEventIds = processedEventIds;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class PersistentSubscriptionConfirmation : Message {
		public readonly Guid CorrelationId;
		public readonly long LastIndexedPosition;
		public readonly long? LastEventNumber;
		public readonly string SubscriptionId;

		public PersistentSubscriptionConfirmation(string subscriptionId, Guid correlationId,
			long lastIndexedPosition, long? lastEventNumber) {
			CorrelationId = correlationId;
			LastIndexedPosition = lastIndexedPosition;
			LastEventNumber = lastEventNumber;
			SubscriptionId = subscriptionId;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReplayParkedMessages : ReadRequestMessage {
		public readonly string EventStreamId;
		public readonly string GroupName;
		public readonly long? StopAt;

		public ReplayParkedMessages(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			string eventStreamId, string groupName, long? stopAt, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			EventStreamId = eventStreamId;
			GroupName = groupName;
			StopAt = stopAt;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReplayParkedMessage : ReadRequestMessage {
		public readonly string EventStreamId;
		public readonly string GroupName;
		public readonly ResolvedEvent Event;

		public ReplayParkedMessage(Guid internalCorrId, Guid correlationId, IEnvelope envelope, string streamId,
			string groupName, ResolvedEvent @event, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			EventStreamId = streamId;
			GroupName = groupName;
			Event = @event;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ReplayMessagesReceived : ReadResponseMessage {
		public readonly Guid CorrelationId;
		public readonly string Reason;
		public readonly ReplayMessagesReceivedResult Result;

		public ReplayMessagesReceived(Guid correlationId, ReplayMessagesReceivedResult result, string reason) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Result = result;
			Reason = reason;
		}

		public enum ReplayMessagesReceivedResult {
			Success = 0,
			DoesNotExist = 1,
			Fail = 2,
			AccessDenied = 3
		}
	}

	//End of persistence subscriptions


	[DerivedMessage(CoreMessage.Client)]
	public partial class SubscribeToStream : ReadRequestMessage {
		public readonly Guid ConnectionId;
		public readonly string EventStreamId; // should be empty to subscribe to all
		public readonly bool ResolveLinkTos;

		public SubscribeToStream(Guid internalCorrId, Guid correlationId, IEnvelope envelope, Guid connectionId,
			string eventStreamId, bool resolveLinkTos, ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			Ensure.NotEmptyGuid(connectionId, "connectionId");
			ConnectionId = connectionId;
			EventStreamId = eventStreamId;
			ResolveLinkTos = resolveLinkTos;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class FilteredSubscribeToStream : ReadRequestMessage {
		public readonly Guid ConnectionId;
		public readonly string EventStreamId; // should be empty to subscribe to all
		public readonly bool ResolveLinkTos;
		public readonly IEventFilter EventFilter;
		public readonly int CheckpointInterval;
		public readonly int CheckpointIntervalCurrent;

		public FilteredSubscribeToStream(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			Guid connectionId, string eventStreamId, bool resolveLinkTos, ClaimsPrincipal user,
			IEventFilter eventFilter, int checkpointInterval, int checkpointIntervalCurrent, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
			Ensure.NotEmptyGuid(connectionId, "connectionId");
			ConnectionId = connectionId;
			EventStreamId = eventStreamId;
			ResolveLinkTos = resolveLinkTos;
			EventFilter = eventFilter;
			CheckpointInterval = checkpointInterval;
			CheckpointIntervalCurrent = checkpointIntervalCurrent;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class CheckpointReached : Message {
		public readonly Guid CorrelationId;
		public readonly TFPos? Position;

		public CheckpointReached(Guid correlationId, TFPos? position) {
			CorrelationId = correlationId;
			Position = position;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class UnsubscribeFromStream : ReadRequestMessage {
		public UnsubscribeFromStream(Guid internalCorrId, Guid correlationId, IEnvelope envelope,
			ClaimsPrincipal user, DateTime? expires = null)
			: base(internalCorrId, correlationId, envelope, user, expires) {
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class SubscriptionConfirmation : Message {
		public readonly Guid CorrelationId;
		public readonly long LastIndexedPosition;
		public readonly long? LastEventNumber;

		public SubscriptionConfirmation(Guid correlationId, long lastIndexedPosition, long? lastEventNumber) {
			CorrelationId = correlationId;
			LastIndexedPosition = lastIndexedPosition;
			LastEventNumber = lastEventNumber;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class StreamEventAppeared : Message {
		public readonly Guid CorrelationId;
		public readonly ResolvedEvent Event;

		public StreamEventAppeared(Guid correlationId, ResolvedEvent @event) {
			CorrelationId = correlationId;
			Event = @event;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class PersistentSubscriptionStreamEventAppeared : Message {
		public readonly Guid CorrelationId;
		public readonly ResolvedEvent Event;
		public readonly int RetryCount;

		public PersistentSubscriptionStreamEventAppeared(Guid correlationId, ResolvedEvent @event, int retryCount) {
			CorrelationId = correlationId;
			Event = @event;
			RetryCount = retryCount;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class SubscriptionDropped : Message {
		public readonly Guid CorrelationId;
		public readonly SubscriptionDropReason Reason;

		public SubscriptionDropped(Guid correlationId, SubscriptionDropReason reason) {
			CorrelationId = correlationId;
			Reason = reason;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class MergeIndexes : Message {
		public readonly IEnvelope Envelope;
		public readonly Guid CorrelationId;
		public readonly ClaimsPrincipal User;

		public MergeIndexes(IEnvelope envelope, Guid correlationId, ClaimsPrincipal user) {
			Ensure.NotNull(envelope, "envelope");
			Envelope = envelope;
			CorrelationId = correlationId;
			User = user;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class MergeIndexesResponse : Message {
		public readonly Guid CorrelationId;
		public readonly MergeIndexesResult Result;

		public MergeIndexesResponse(Guid correlationId, MergeIndexesResult result) {
			CorrelationId = correlationId;
			Result = result;
		}

		public override string ToString() {
			return String.Format("Result: {0}", Result);
		}

		public enum MergeIndexesResult {
			Started
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class SetNodePriority : Message {
		public readonly int NodePriority;

		public SetNodePriority(int nodePriority) {
			NodePriority = nodePriority;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ResignNode : Message {
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ScavengeDatabase : Message {
		public readonly IEnvelope Envelope;
		public readonly Guid CorrelationId;
		public readonly ClaimsPrincipal User;
		public readonly int StartFromChunk;
		public readonly int Threads;
		public readonly int? Threshold;
		public readonly int? ThrottlePercent;
		public readonly bool SyncOnly;

		public ScavengeDatabase(
			IEnvelope envelope,
			Guid correlationId,
			ClaimsPrincipal user,
			int startFromChunk,
			int threads,
			int? threshold,
			int? throttlePercent,
			bool syncOnly) {

			Ensure.NotNull(envelope, "envelope");
			Envelope = envelope;
			CorrelationId = correlationId;
			User = user;
			StartFromChunk = startFromChunk;
			Threads = threads;
			Threshold = threshold;
			ThrottlePercent = throttlePercent;
			SyncOnly = syncOnly;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class StopDatabaseScavenge : Message {
		public readonly IEnvelope Envelope;
		public readonly Guid CorrelationId;
		public readonly ClaimsPrincipal User;
		public readonly string ScavengeId;

		public StopDatabaseScavenge(IEnvelope envelope, Guid correlationId, ClaimsPrincipal user, string scavengeId) {
			Ensure.NotNull(envelope, "envelope");
			Envelope = envelope;
			CorrelationId = correlationId;
			User = user;
			ScavengeId = scavengeId;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class GetCurrentDatabaseScavenge : Message {
		public readonly IEnvelope Envelope;
		public readonly Guid CorrelationId;
		public readonly ClaimsPrincipal User;

		public GetCurrentDatabaseScavenge(IEnvelope envelope, Guid correlationId, ClaimsPrincipal user) {
			Ensure.NotNull(envelope, "envelope");
			Envelope = envelope;
			CorrelationId = correlationId;
			User = user;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ScavengeDatabaseGetCurrentResponse : Message {
		public readonly Guid CorrelationId;
		public readonly ScavengeResult Result;
		public readonly string ScavengeId;

		public ScavengeDatabaseGetCurrentResponse(Guid correlationId,
			ScavengeResult result, string scavengeId) {
			CorrelationId = correlationId;
			Result = result;
			ScavengeId = scavengeId;
		}

		public override string ToString() => $"Result: {Result}, ScavengeId: {ScavengeId}";
		public enum ScavengeResult {
			InProgress,
			Stopped
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class GetLastDatabaseScavenge : Message {
		public readonly IEnvelope Envelope;
		public readonly Guid CorrelationId;
		public readonly ClaimsPrincipal User;

		public GetLastDatabaseScavenge(IEnvelope envelope, Guid correlationId, ClaimsPrincipal user) {
			Ensure.NotNull(envelope, nameof(envelope));
			Envelope = envelope;
			CorrelationId = correlationId;
			User = user;
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ScavengeDatabaseGetLastResponse : Message {
		public readonly Guid CorrelationId;
		public readonly ScavengeResult Result;
		public readonly string ScavengeId;

		public ScavengeDatabaseGetLastResponse(
			Guid correlationId,
			ScavengeResult result,
			string scavengeId) {
			CorrelationId = correlationId;
			Result = result;
			ScavengeId = scavengeId;
		}

		public override string ToString() => $"Result: {Result}, ScavengeId: {ScavengeId}";

		public enum ScavengeResult {
			Unknown,
			InProgress,
			Success,
			Stopped,
			Errored,
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ScavengeDatabaseStartedResponse : Message {
		public readonly Guid CorrelationId;
		public readonly string ScavengeId;

		public ScavengeDatabaseStartedResponse(Guid correlationId, string scavengeId) {
			CorrelationId = correlationId;
			ScavengeId = scavengeId;
		}
		public override string ToString() => $"ScavengeId: {ScavengeId}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ScavengeDatabaseInProgressResponse : Message {
		public readonly Guid CorrelationId;
		public readonly string ScavengeId;
		public readonly string Reason;

		public ScavengeDatabaseInProgressResponse(Guid correlationId, string scavengeId, string reason) {
			CorrelationId = correlationId;
			ScavengeId = scavengeId;
			Reason = reason;
		}

		public override string ToString() => $"ScavengeId: {ScavengeId}, Reason: {Reason}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ScavengeDatabaseStoppedResponse : Message {
		public readonly Guid CorrelationId;
		public readonly string ScavengeId;

		public ScavengeDatabaseStoppedResponse(Guid correlationId, string scavengeId) {
			CorrelationId = correlationId;
			ScavengeId = scavengeId;
		}

		public override string ToString() => $"ScavengeId: {ScavengeId}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ScavengeDatabaseNotFoundResponse : Message {
		public readonly Guid CorrelationId;
		public readonly string ScavengeId;
		public readonly string Reason;

		public ScavengeDatabaseNotFoundResponse(Guid correlationId, string scavengeId, string reason) {
			CorrelationId = correlationId;
			ScavengeId = scavengeId;
			Reason = reason;
		}

		public override string ToString() => $"ScavengeId: {ScavengeId}, Reason: {Reason}";

	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ScavengeDatabaseUnauthorizedResponse : Message {
		public readonly Guid CorrelationId;
		public readonly string ScavengeId;
		public readonly string Reason;

		public ScavengeDatabaseUnauthorizedResponse(Guid correlationId, string scavengeId, string reason) {
			CorrelationId = correlationId;
			ScavengeId = scavengeId;
			Reason = reason;
		}
		public override string ToString() => $"ScavengeId: {ScavengeId}, Reason: {Reason}";
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class IdentifyClient : Message {
		public readonly Guid CorrelationId;
		public readonly int Version;
		public readonly string ConnectionName;

		public IdentifyClient(Guid correlationId,
			int version,
			string connectionName) {
			CorrelationId = correlationId;
			Version = version;
			ConnectionName = connectionName;
		}

		public override string ToString() {
			return String.Format("Version: {0}, Connection Name: {1}", Version, ConnectionName);
		}
	}

	[DerivedMessage(CoreMessage.Client)]
	public partial class ClientIdentified : Message {
		public readonly Guid CorrelationId;

		public ClientIdentified(Guid correlationId) {
			CorrelationId = correlationId;
		}
	}
}
