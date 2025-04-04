// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Checkpoint;

namespace EventStore.Core.TransactionLog.Chunks;

public class TFChunkDbConfig {
	public readonly string Path;
	public readonly int ChunkSize;
	public readonly long MaxChunksCacheSize;
	public readonly ICheckpoint WriterCheckpoint;
	public readonly ICheckpoint ChaserCheckpoint;
	public readonly ICheckpoint EpochCheckpoint;
	public readonly ICheckpoint ProposalCheckpoint;
	public readonly ICheckpoint TruncateCheckpoint;
	public readonly ICheckpoint ReplicationCheckpoint;
	public readonly ICheckpoint IndexCheckpoint;
	public readonly ICheckpoint StreamExistenceFilterCheckpoint;
	public readonly bool InMemDb;
	public readonly bool Unbuffered;
	public readonly bool WriteThrough;
	public readonly bool ReduceFileCachePressure;
	public readonly long MaxTruncation;

	public TFChunkDbConfig(string path,
		int chunkSize,
		long maxChunksCacheSize,
		ICheckpoint writerCheckpoint,
		ICheckpoint chaserCheckpoint,
		ICheckpoint epochCheckpoint,
		ICheckpoint proposalCheckpoint,
		ICheckpoint truncateCheckpoint,
		ICheckpoint replicationCheckpoint,
		ICheckpoint indexCheckpoint,
		ICheckpoint streamExistenceFilterCheckpoint,
		bool inMemDb = false,
		bool unbuffered = false,
		bool writethrough = false,
		bool reduceFileCachePressure = false,
		long maxTruncation = 256 * 1024 * 1024) {
		Ensure.NotNullOrEmpty(path, "path");
		Ensure.Positive(chunkSize, "chunkSize");
		Ensure.Nonnegative(maxChunksCacheSize, "maxChunksCacheSize");
		Ensure.NotNull(writerCheckpoint, "writerCheckpoint");
		Ensure.NotNull(chaserCheckpoint, "chaserCheckpoint");
		Ensure.NotNull(epochCheckpoint, "epochCheckpoint");
		Ensure.NotNull(proposalCheckpoint, "proposalCheckpoint");
		Ensure.NotNull(truncateCheckpoint, "truncateCheckpoint");
		Ensure.NotNull(replicationCheckpoint, "replicationCheckpoint");
		Ensure.NotNull(indexCheckpoint, "indexCheckpoint");
		Ensure.NotNull(streamExistenceFilterCheckpoint, "streamExistenceFilterCheckpoint");

		Path = path;
		ChunkSize = chunkSize;
		MaxChunksCacheSize = maxChunksCacheSize;
		WriterCheckpoint = writerCheckpoint;
		ChaserCheckpoint = chaserCheckpoint;
		EpochCheckpoint = epochCheckpoint;
		ProposalCheckpoint = proposalCheckpoint;
		TruncateCheckpoint = truncateCheckpoint;
		ReplicationCheckpoint = replicationCheckpoint;
		IndexCheckpoint = indexCheckpoint;
		StreamExistenceFilterCheckpoint = streamExistenceFilterCheckpoint;
		InMemDb = inMemDb;
		Unbuffered = unbuffered;
		WriteThrough = writethrough;
		ReduceFileCachePressure = reduceFileCachePressure;
		MaxTruncation = maxTruncation;
	}
}
