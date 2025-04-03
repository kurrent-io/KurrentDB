// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Scavenging;

// The checkpoint stores which scavengepoint we are processing and where we are up to with it.

public abstract class ScavengeCheckpoint {
	protected ScavengeCheckpoint(ScavengePoint scavengePoint) {
		ScavengePoint = scavengePoint;
	}

	public ScavengePoint ScavengePoint { get; }

	public class Accumulating : ScavengeCheckpoint {
		// Accumulating with null doneLogicalChunkNumber means we are accumulating now but havent
		// accumulated anything.
		public Accumulating(ScavengePoint scavengePoint, int? doneLogicalChunkNumber)
			: base(scavengePoint) {
			DoneLogicalChunkNumber = doneLogicalChunkNumber;
		}

		public int? DoneLogicalChunkNumber { get; }

		public override string ToString() =>
			$"Accumulating {ScavengePoint.GetName()} done " +
				(DoneLogicalChunkNumber.HasValue
					? $"Chunk {DoneLogicalChunkNumber}"
					: "None");
	}

	public class Calculating<TStreamId> : ScavengeCheckpoint {
		public Calculating(ScavengePoint scavengePoint, StreamHandle<TStreamId> doneStreamHandle)
			: base(scavengePoint) {
			DoneStreamHandle = doneStreamHandle;
		}

		public StreamHandle<TStreamId> DoneStreamHandle { get; }

		public override string ToString() =>
			$"Calculating {ScavengePoint.GetName()} done {DoneStreamHandle}";
	}

	public class ExecutingChunks : ScavengeCheckpoint {
		public int? DoneLogicalChunkNumber { get; }

		public ExecutingChunks(ScavengePoint scavengePoint, int? doneLogicalChunkNumber)
			: base(scavengePoint) {
			DoneLogicalChunkNumber = doneLogicalChunkNumber;
		}

		public override string ToString() =>
			$"Executing chunks for {ScavengePoint.GetName()} done " +
				(DoneLogicalChunkNumber.HasValue
					? $"Chunk {DoneLogicalChunkNumber}"
					: "None");
	}

	public class ExecutingIndex : ScavengeCheckpoint {
		public ExecutingIndex(ScavengePoint scavengePoint)
			: base(scavengePoint) {
		}

		public override string ToString() =>
			$"Executing index for {ScavengePoint.GetName()}";
	}

	public class MergingChunks : ScavengeCheckpoint {
		public MergingChunks(ScavengePoint scavengePoint)
			: base(scavengePoint) {
		}

		public override string ToString() =>
			$"Merging chunks for {ScavengePoint.GetName()}";
	}

	public class Cleaning : ScavengeCheckpoint {
		public Cleaning(ScavengePoint scavengePoint)
			: base(scavengePoint) {
		}

		public override string ToString() =>
			$"Cleaning for {ScavengePoint.GetName()}";
	}

	public class Done : ScavengeCheckpoint {
		public Done(ScavengePoint scavengePoint)
			: base(scavengePoint) {
		}

		public override string ToString() =>
			$"Done {ScavengePoint.GetName()}";
	}
}
