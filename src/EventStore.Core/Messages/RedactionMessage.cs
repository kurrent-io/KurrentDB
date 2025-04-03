// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Data.Redaction;
using EventStore.Core.Messaging;

namespace EventStore.Core.Messages;

public static partial class RedactionMessage {
	[DerivedMessage(CoreMessage.Redaction)]
	public partial class GetEventPosition : Message {
		public IEnvelope Envelope { get; }
		public string EventStreamId { get; }
		public long EventNumber { get; }

		public GetEventPosition(IEnvelope envelope, string eventStreamId, long eventNumber) {
			Ensure.NotNull(envelope, nameof(envelope));
			Ensure.NotNullOrEmpty(eventStreamId, nameof(eventStreamId));
			Ensure.Nonnegative(eventNumber, nameof(eventNumber));

			Envelope = envelope;
			EventStreamId = eventStreamId;
			EventNumber = eventNumber;
		}
	}

	[DerivedMessage(CoreMessage.Redaction)]
	public partial class GetEventPositionCompleted : Message {
		public GetEventPositionResult Result { get; }
		public EventPosition[] EventPositions { get; }

		public GetEventPositionCompleted(GetEventPositionResult result, EventPosition[] eventPositions) {
			Ensure.NotNull(eventPositions, nameof(eventPositions));

			Result = result;
			EventPositions = eventPositions;
		}
	}

	[DerivedMessage(CoreMessage.Redaction)]
	public partial class AcquireChunksLock : Message {
		public IEnvelope Envelope { get; }

		public AcquireChunksLock(IEnvelope envelope) {
			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Redaction)]
	public partial class AcquireChunksLockCompleted : Message {
		public AcquireChunksLockResult Result { get; }
		public Guid AcquisitionId { get; }

		public AcquireChunksLockCompleted(AcquireChunksLockResult result, Guid acquisitionId) {
			Result = result;
			AcquisitionId = acquisitionId;
		}
	}

	[DerivedMessage(CoreMessage.Redaction)]
	public partial class SwitchChunk : Message {
		public IEnvelope Envelope { get; }
		public Guid AcquisitionId { get; }
		public string TargetChunkFile { get; }
		public string NewChunkFile { get; }

		public SwitchChunk(IEnvelope envelope, Guid acquisitionId, string targetChunkFile, string newChunkFile) {
			Ensure.NotNull(envelope, nameof(envelope));
			Ensure.NotEmptyGuid(acquisitionId, nameof(acquisitionId));
			Ensure.NotNullOrEmpty(targetChunkFile, nameof(targetChunkFile));
			Ensure.NotNullOrEmpty(newChunkFile, nameof(newChunkFile));

			Envelope = envelope;
			AcquisitionId = acquisitionId;
			TargetChunkFile = targetChunkFile;
			NewChunkFile = newChunkFile;
		}
	}

	[DerivedMessage(CoreMessage.Redaction)]
	public partial class SwitchChunkCompleted : Message {
		public SwitchChunkResult Result { get; }

		public SwitchChunkCompleted(SwitchChunkResult result) {
			Result = result;
		}
	}

	[DerivedMessage(CoreMessage.Redaction)]
	public partial class ReleaseChunksLock : Message {
		public IEnvelope Envelope { get; }
		public Guid AcquisitionId { get; }

		public ReleaseChunksLock(IEnvelope envelope, Guid acquisitionId) {
			Ensure.NotNull(envelope, nameof(envelope));
			Ensure.NotEmptyGuid(acquisitionId, nameof(acquisitionId));
			Envelope = envelope;
			AcquisitionId = acquisitionId;
		}
	}

	[DerivedMessage(CoreMessage.Redaction)]
	public partial class ReleaseChunksLockCompleted : Message {
		public ReleaseChunksLockResult Result { get; }

		public ReleaseChunksLockCompleted(ReleaseChunksLockResult result) {
			Result = result;
		}
	}
}
