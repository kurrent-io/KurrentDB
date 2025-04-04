// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;

public class FakeTFScavengerLog : ITFChunkScavengerLog {
	public string ScavengeId { get; } = "FakeScavenge";

	public long SpaceSaved { get; } = 0;

	public bool AlwaysKeepScavenged { get; private set; }
	public bool MergeChunks { get; private set; }
	public int StartFromChunk { get; private set; }
	public int Threads { get; private set; }
	public bool Started { get; private set; }

	public bool Completed { get; private set; }

	public string Error { get; private set; }

	public ScavengeResult Result { get; private set; }

	public event EventHandler<EventArgs> StartedCallback;
	public event EventHandler<ScavengedLog> ChunkScavenged;
	public event EventHandler<IndexScavengedLog> IndexScavenged;
	public event EventHandler<EventArgs> CompletedCallback;

	public IList<ScavengedLog> Scavenged { get; } = new List<ScavengedLog>();
	public IList<ScavengedLog> Merged { get; } = new List<ScavengedLog>();
	public IList<IndexScavengedLog> ScavengedIndices { get; } = new List<IndexScavengedLog>();

	public void ScavengeStarted() {
		Started = true;
		StartedCallback?.Invoke(this, EventArgs.Empty);
	}

	public void ScavengeStarted(bool alwaysKeepScavenged, bool mergeChunks, int startFromChunk, int threads) {
		AlwaysKeepScavenged = alwaysKeepScavenged;
		MergeChunks = mergeChunks;
		StartFromChunk = startFromChunk;
		Threads = threads;
		Started = true;
		StartedCallback?.Invoke(this, EventArgs.Empty);
	}

	public void ChunksScavenged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, long spaceSaved) {
		var scavengedLog = new ScavengedLog(chunkStartNumber, chunkEndNumber, true, "");
		Scavenged.Add(scavengedLog);
		ChunkScavenged?.Invoke(this, scavengedLog);
	}

	public void ChunksNotScavenged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed,
		string errorMessage) {
		var scavengedLog = new ScavengedLog(chunkStartNumber, chunkEndNumber, false, "");
		Scavenged.Add(scavengedLog);
		ChunkScavenged?.Invoke(this, scavengedLog);
	}

	public void ChunksMerged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, long spaceSaved) {
		var scavengedLog = new ScavengedLog(chunkStartNumber, chunkEndNumber, true, "");
		Merged.Add(scavengedLog);
		ChunkScavenged?.Invoke(this, scavengedLog);
	}

	public void ChunksNotMerged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, string errorMessage) {
		var scavengedLog = new ScavengedLog(chunkStartNumber, chunkEndNumber, false, "");
		Merged.Add(scavengedLog);
		ChunkScavenged?.Invoke(this, scavengedLog);
	}

	public void IndexTableScavenged(int level, int index, TimeSpan elapsed, long entriesDeleted, long entriesKept,
		long spaceSaved) {
		var indexScavenged = new IndexScavengedLog(true, null, entriesDeleted);
		ScavengedIndices.Add(indexScavenged);
		IndexScavenged?.Invoke(this, indexScavenged);
	}

	public void IndexTableNotScavenged(int level, int index, TimeSpan elapsed, long entriesKept,
		string errorMessage) {
		var indexScavenged = new IndexScavengedLog(false, errorMessage, 0);
		ScavengedIndices.Add(indexScavenged);
		IndexScavenged?.Invoke(this, indexScavenged);
	}

	public void ScavengeCompleted(ScavengeResult result, string error, TimeSpan elapsed) {
		Completed = true;
		Result = result;
		Error = error;
		CompletedCallback?.Invoke(this, EventArgs.Empty);
	}

	public class ScavengedLog {
		public int ChunkStart { get; }
		public int ChunkEnd { get; }
		public bool Scavenged { get; }
		public string Error { get; }

		public ScavengedLog(int chunkStart, int chunkEnd, bool scavenged, string error) {
			ChunkStart = chunkStart;
			ChunkEnd = chunkEnd;
			Scavenged = scavenged;
			Error = error;
		}
	}

	public class IndexScavengedLog {
		public bool Scavenged { get; }
		public string Error { get; }
		public long EntriesDeleted { get; }

		public IndexScavengedLog(bool scavenged, string error, long entriesDeleted) {
			Scavenged = scavenged;
			Error = error;
			EntriesDeleted = entriesDeleted;
		}
	}
}
