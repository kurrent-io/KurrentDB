// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using EventStore.Core.Messages;
using EventStore.Core.Services.Archive;

namespace EventStore.Core.TransactionLog.Scavenging;

// Calculates the way to configure the scavenge based on various configuration sources
public class ScavengeOptionsCalculator {
	readonly ClusterVNodeOptions _vNodeOptions;
	readonly ArchiveOptions _archiveOptions;
	readonly ClientMessage.ScavengeDatabase _message;

	public ScavengeOptionsCalculator(
		ClusterVNodeOptions vNodeOptions,
		ArchiveOptions archiveOptions,
		ClientMessage.ScavengeDatabase message) {

		_vNodeOptions = vNodeOptions;
		_archiveOptions = archiveOptions;
		_message = message;
	}

	// Archiving disables chunk merging because the two in combination make things
	// more complex (locating chunks in the archive is more complicated if they
	// are merged, and there may be subtleties if we try to merge a local chunk
	// into a chunk that is located in the archive.
	// Merging is also less important with archiving - we shouldn't have too many
	// chunks locally because of the archive, and it is ok to have lots of physical
	// files in the archive because we do not open file handles to all of them
	// continuously
	public bool MergeChunks =>
		!_archiveOptions.Enabled &&
		!_vNodeOptions.Database.DisableScavengeMerging;

	public int ChunkExecutionThreshold => _message.Threshold ?? 0;
}
