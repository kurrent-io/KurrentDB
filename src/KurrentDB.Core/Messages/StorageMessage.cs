// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Messages;

public static partial class StorageMessage {
	public interface IPreconditionedWriteMessage {
		Guid CorrelationId { get; }
		IEnvelope Envelope { get; }
	}

	public interface IFlushableMessage {
	}

	public interface ILeaderWriteMessage {
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class WritePrepares : Message, IPreconditionedWriteMessage, IFlushableMessage, ILeaderWriteMessage {
		public Guid CorrelationId { get; private set; }
		public IEnvelope Envelope { get; private set; }
		public LowAllocReadOnlyMemory<string> EventStreamIds { get; private set; }
		public LowAllocReadOnlyMemory<long> ExpectedVersions { get; private set; }
		public LowAllocReadOnlyMemory<Event> Events { get; private set; }
		public LowAllocReadOnlyMemory<int> EventStreamIndexes { get; private set; }

		public WritePrepares(
			Guid correlationId,
			IEnvelope envelope,
			LowAllocReadOnlyMemory<string> eventStreamIds,
			LowAllocReadOnlyMemory<long> expectedVersions,
			LowAllocReadOnlyMemory<Event> events,
			LowAllocReadOnlyMemory<int> eventStreamIndexes,
			CancellationToken cancellationToken) : base(cancellationToken) {
			CorrelationId = correlationId;
			Envelope = envelope;
			EventStreamIds = eventStreamIds;
			ExpectedVersions = expectedVersions;
			Events = events;
			EventStreamIndexes = eventStreamIndexes;
		}

		public override string ToString() {
			var sumDataBytes = 0L;
			var sumMetadataBytes = 0L;
			foreach (var @event in Events.Span) {
				sumDataBytes += @event.Data.Length;
				sumMetadataBytes += @event.Metadata.Length;
			}

			return $"{GetType().Name} " +
				$"CorrelationId: {CorrelationId}, " +
				$"EventStreamIds: {string.Join(", ", EventStreamIds.ToArray())}, " + // TODO: use .Span instead of .ToArray() when we move to .NET 10
				$"ExpectedVersions: {string.Join(", ", ExpectedVersions.ToArray())}, " +
				$"Envelope: {{ {Envelope} }}, " +
				$"NumEvents: {Events.Length}, " +
				$"DataBytes: {sumDataBytes}, " +
				$"MetadataBytes: {sumMetadataBytes}";
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class WriteDelete : Message, IPreconditionedWriteMessage, IFlushableMessage, ILeaderWriteMessage {
		public Guid CorrelationId { get; private set; }
		public IEnvelope Envelope { get; private set; }
		public string EventStreamId { get; private set; }
		public long ExpectedVersion { get; private set; }
		public readonly bool HardDelete;

		public WriteDelete(Guid correlationId, IEnvelope envelope, string eventStreamId, long expectedVersion,
			bool hardDelete, CancellationToken cancellationToken) : base(cancellationToken) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			Ensure.NotNull(envelope, "envelope");
			Ensure.NotNull(eventStreamId, "eventStreamId");

			CorrelationId = correlationId;
			Envelope = envelope;
			EventStreamId = eventStreamId;
			ExpectedVersion = expectedVersion;
			HardDelete = hardDelete;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class WriteCommit : Message, IFlushableMessage, ILeaderWriteMessage {
		public readonly Guid CorrelationId;
		public readonly IEnvelope Envelope;
		public readonly long TransactionPosition;

		public WriteCommit(Guid correlationId, IEnvelope envelope, long transactionPosition) {
			CorrelationId = correlationId;
			Envelope = envelope;
			TransactionPosition = transactionPosition;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class WriteTransactionStart : Message, IPreconditionedWriteMessage, IFlushableMessage,
		ILeaderWriteMessage {

		public Guid CorrelationId { get; private set; }
		public IEnvelope Envelope { get; private set; }
		public string EventStreamId { get; private set; }
		public long ExpectedVersion { get; private set; }

		public readonly DateTime LiveUntil;

		public WriteTransactionStart(Guid correlationId, IEnvelope envelope, string eventStreamId,
			long expectedVersion, DateTime liveUntil) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			Ensure.NotNull(envelope, "envelope");
			Ensure.NotNull(eventStreamId, "eventStreamId");

			CorrelationId = correlationId;
			Envelope = envelope;
			EventStreamId = eventStreamId;
			ExpectedVersion = expectedVersion;

			LiveUntil = liveUntil;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class WriteTransactionData : Message, IFlushableMessage, ILeaderWriteMessage {
		public readonly Guid CorrelationId;
		public readonly IEnvelope Envelope;
		public readonly long TransactionId;
		public readonly Event[] Events;

		public WriteTransactionData(Guid correlationId, IEnvelope envelope, long transactionId, Event[] events) {
			CorrelationId = correlationId;
			Envelope = envelope;
			TransactionId = transactionId;
			Events = events;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class WriteTransactionEnd : Message, IFlushableMessage, ILeaderWriteMessage {
		public readonly Guid CorrelationId;
		public readonly IEnvelope Envelope;
		public readonly long TransactionId;

		public readonly DateTime LiveUntil;

		public WriteTransactionEnd(Guid correlationId, IEnvelope envelope, long transactionId,
			DateTime liveUntil) {
			CorrelationId = correlationId;
			Envelope = envelope;
			TransactionId = transactionId;

			LiveUntil = liveUntil;
		}
	}

	/// <summary>
	/// Sent by the StorageChaser when it chases a prepare that is not self committing
	/// Handled by RequestManagementService
	/// </summary>
	[DerivedMessage(CoreMessage.Storage)]
	public partial class UncommittedPrepareChased : Message {
		public readonly Guid CorrelationId;
		public readonly long LogPosition;
		public readonly PrepareFlags Flags;

		public UncommittedPrepareChased(Guid correlationId, long logPosition, PrepareFlags flags) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			Ensure.Nonnegative(logPosition, "logPosition");

			CorrelationId = correlationId;
			LogPosition = logPosition;
			Flags = flags;
		}
	}

	/// <summary>
	/// Sent by the StorageChaser when it chases a commit log record or a prepare that is self committing and TxEnd
	/// Received by the IndexCommitterService
	/// </summary>
	[DerivedMessage(CoreMessage.Storage)]
	public partial class CommitChased : Message {
		public readonly Guid CorrelationId;
		public readonly long LogPosition;
		public readonly long TransactionPosition;
		public readonly int NumStreams;
		public readonly LowAllocReadOnlyMemory<int> EventStreamIndexes; // [] => single stream, index 0  //qq try to remove this

		public CommitChased(Guid correlationId, long logPosition, long transactionPosition,
			int numStreams,
			LowAllocReadOnlyMemory<int> eventStreamIndexes) {

			Ensure.NotEmptyGuid(correlationId, "correlationId");
			Ensure.Nonnegative(logPosition, "logPosition");
			Ensure.Nonnegative(transactionPosition, "transactionPosition");

			foreach (var eventStreamIndex in eventStreamIndexes.Span) {
				if (eventStreamIndex < 0 || eventStreamIndex >= numStreams)
					throw new ArgumentOutOfRangeException(nameof(eventStreamIndexes));
			}

			CorrelationId = correlationId;
			LogPosition = logPosition;
			TransactionPosition = transactionPosition;
			NumStreams = numStreams;
			EventStreamIndexes = eventStreamIndexes;
		}

		// used in tests only
		public static CommitChased ForSingleStream(Guid correlationId, long logPosition, long transactionPosition) {
			return new CommitChased(
				correlationId,
				logPosition,
				transactionPosition,
				numStreams: 1,
				eventStreamIndexes: []);
		}
	}

	/// <summary>
	/// Sent by the IndexCommitterService after the the log is indexed up to a CommitAck
	/// Received by the RequestManagementService
	/// </summary>
	[DerivedMessage(CoreMessage.Storage)]
	public partial class CommitIndexed : Message {
		public readonly Guid CorrelationId;
		public readonly long LogPosition;
		public readonly long TransactionPosition;

		public CommitIndexed(Guid correlationId, long logPosition, long transactionPosition) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			Ensure.Nonnegative(logPosition, "logPosition");
			Ensure.Nonnegative(transactionPosition, "transactionPosition");

			CorrelationId = correlationId;
			LogPosition = logPosition;
			TransactionPosition = transactionPosition;
		}

		// used in tests only
		public static CommitIndexed ForSingleStream(Guid correlationId, long logPosition, long transactionPosition) {
			return new CommitIndexed(
				correlationId,
				logPosition,
				transactionPosition);
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class EventCommitted : Message {
		public readonly long CommitPosition;
		public readonly EventRecord Event;
		public readonly bool TfEof;

		public EventCommitted(long commitPosition, EventRecord @event, bool isTfEof) {
			CommitPosition = commitPosition;
			Event = @event;
			TfEof = isTfEof;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class SecondaryIndexCommitted(string indexName, ResolvedEvent @event) : Message {
		public readonly ResolvedEvent Event = @event;
		public readonly string IndexName = indexName;
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class SecondaryIndexDeleted(Regex streamIdRegex) : Message {
		public readonly Regex StreamIdRegex = streamIdRegex;
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class InMemoryEventCommitted : Message {
		public readonly long CommitPosition;
		public readonly EventRecord Event;

		public InMemoryEventCommitted(long commitPosition, EventRecord @event) {
			CommitPosition = commitPosition;
			Event = @event;
		}
	}

	// This message is a notification that primary indexing has reached the end of the transaction log.
	// Normally this is indicated with an EventCommitted message that has the TfEof flag set to true,
	// but this message is sent instead when (1) the end of the log is not an Event at all (2) after the initial index catchup.
	[DerivedMessage(CoreMessage.Storage)]
	public partial class IndexedToEndOfTransactionFile : Message {
		public IndexedToEndOfTransactionFile() {
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class AlreadyCommitted : Message {
		public readonly Guid CorrelationId;

		public readonly LowAllocReadOnlyMemory<long> FirstEventNumbers;
		public readonly LowAllocReadOnlyMemory<long> LastEventNumbers;
		public readonly long LogPosition;

		public AlreadyCommitted(Guid correlationId,
			LowAllocReadOnlyMemory<long> firstEventNumbers,
			LowAllocReadOnlyMemory<long> lastEventNumbers,
			long logPosition) {
			Ensure.NotEmptyGuid(correlationId, nameof(correlationId));
			Ensure.Equal(lastEventNumbers.Length, firstEventNumbers.Length, nameof(lastEventNumbers));

			CorrelationId = correlationId;
			FirstEventNumbers = firstEventNumbers;
			LastEventNumbers = lastEventNumbers;
			LogPosition = logPosition;
		}

		// used in tests only
		public static AlreadyCommitted ForSingleStream(Guid correlationId, string eventStreamId, long firstEventNumber, long lastEventNumber, long logPosition) {
			return new AlreadyCommitted(
				correlationId,
				firstEventNumbers: new(firstEventNumber),
				lastEventNumbers: new(lastEventNumber),
				logPosition);
		}

		public override string ToString() {
			return
				$"CorrelationId: {CorrelationId}," +
				$"FirstEventNumbers: {string.Join(", ", FirstEventNumbers.ToArray())}," +
				$"LastEventNumbers: {string.Join(", ", LastEventNumbers.ToArray())}";
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class InvalidTransaction : Message {
		public readonly Guid CorrelationId;

		public InvalidTransaction(Guid correlationId) {
			CorrelationId = correlationId;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class ConsistencyChecksSucceeded : Message {
		public readonly Guid CorrelationId;
		public readonly LowAllocReadOnlyMemory<long> FirstEventNumbers;
		public readonly LowAllocReadOnlyMemory<long> LastEventNumbers;

		public ConsistencyChecksSucceeded(Guid correlationId,
			LowAllocReadOnlyMemory<long> firstEventNumbers,
			LowAllocReadOnlyMemory<long> lastEventNumbers) {
			CorrelationId = correlationId;
			FirstEventNumbers = firstEventNumbers;
			LastEventNumbers = lastEventNumbers;
		}

		public static ConsistencyChecksSucceeded ForSingleStream(Guid correlationId, long firstEventNumber, long lastEventNumber) =>
			new(correlationId, new(firstEventNumber), new(lastEventNumber));
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class ConsistencyChecksFailed(
		Guid correlationId,
		LowAllocReadOnlyMemory<ConsistencyCheckFailure> failures) : Message {

		public Guid CorrelationId => correlationId;
		public LowAllocReadOnlyMemory<ConsistencyCheckFailure> Failures => failures;

		public static ConsistencyChecksFailed ForSingleStream(Guid correlationId, long expectedVersion, long actualVersion, bool? isSoftDeleted) =>
			new(correlationId, new(new ConsistencyCheckFailure(0, expectedVersion, actualVersion, isSoftDeleted)));
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class RequestCompleted : Message {
		public readonly Guid CorrelationId;
		public readonly bool Success;

		public RequestCompleted(Guid correlationId, bool success) {
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			CorrelationId = correlationId;
			Success = success;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class RequestManagerTimerTick : Message {
		public DateTime UtcNow {
			get { return _now ?? DateTime.UtcNow; }
		}

		private readonly DateTime? _now;

		public RequestManagerTimerTick() {
		}

		public RequestManagerTimerTick(DateTime now) {
			_now = now;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class BatchLogExpiredMessages : Message {
		public override object Affinity => null;
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class EffectiveStreamAclRequest : Message {
		public readonly string StreamId;
		public readonly IEnvelope Envelope;

		public EffectiveStreamAclRequest(string streamId, IEnvelope envelope, CancellationToken cancellationToken)
			: base(cancellationToken) {
			StreamId = streamId;
			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class EffectiveStreamAclResponse : Message {
		public readonly EffectiveAcl Acl;

		public EffectiveStreamAclResponse(EffectiveAcl acl) {
			Acl = acl;
		}
	}

	public class EffectiveAcl {
		public readonly StreamAcl Stream;
		public readonly StreamAcl System;
		public readonly StreamAcl Default;

		public EffectiveAcl(StreamAcl stream, StreamAcl system, StreamAcl @default) {
			Stream = stream;
			System = system;
			Default = @default;
		}

		public static Task<EffectiveAcl> LoadAsync(IPublisher publisher, string streamId, CancellationToken cancellationToken) {
			var envelope = new RequestEffectiveAclEnvelope();
			publisher.Publish(new EffectiveStreamAclRequest(streamId, envelope, cancellationToken));
			return envelope.Task;
		}

		class RequestEffectiveAclEnvelope : IEnvelope {
			private readonly TaskCompletionSource<EffectiveAcl> _tcs;

			public RequestEffectiveAclEnvelope() {
				_tcs = new TaskCompletionSource<EffectiveAcl>(TaskCreationOptions.RunContinuationsAsynchronously);
			}
			public void ReplyWith<T>(T message) where T : Message {
				if (message == null)
					throw new ArgumentNullException(nameof(message));
				if (message is EffectiveStreamAclResponse response) {
					_tcs.TrySetResult(response.Acl);
					return;
				} else {
					if (message is OperationCancelledMessage cancelled) {
						_tcs.TrySetCanceled(cancelled.CancellationToken);
					}
				}
				throw new ArgumentException($"Unexpected message type {typeof(T)}");
			}

			public Task<EffectiveAcl> Task => _tcs.Task;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class OperationCancelledMessage : Message {
		public OperationCancelledMessage(CancellationToken cancellationToken) : base(cancellationToken) {
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class StreamIdFromTransactionIdRequest : Message {
		public readonly long TransactionId;
		public readonly IEnvelope Envelope;

		public StreamIdFromTransactionIdRequest(in long transactionId, IEnvelope envelope, CancellationToken cancellationToken)
			: base(cancellationToken) {
			TransactionId = transactionId;
			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Storage)]
	public partial class StreamIdFromTransactionIdResponse : Message {
		public readonly string StreamId;

		public StreamIdFromTransactionIdResponse(string streamId) {
			StreamId = streamId;
		}
	}
}
