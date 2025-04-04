// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Index;

namespace EventStore.Core.Tests.Index;

public static class IndexMapExtensions {
	public static MergeResult AddAndMergePTable(
		this IndexMap indexMap,
		PTable tableToAdd,
		int prepareCheckpoint,
		int commitCheckpoint,
		IIndexFilenameProvider filenameProvider,
		byte version,
		int indexCacheDepth = 16,
		bool skipIndexVerify = false,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000) {

		var addResult = indexMap.AddPTable(tableToAdd, prepareCheckpoint, commitCheckpoint);
		if (addResult.CanMergeAny) {
			var toDelete = new List<PTable>();
			MergeResult mergeResult;
			IndexMap curMap = addResult.NewMap;
			do {
				mergeResult = curMap.TryMergeOneLevel(
					filenameProvider,
					version,
					indexCacheDepth,
					skipIndexVerify,
					useBloomFilter,
					lruCacheSize
				);

				curMap = mergeResult.MergedMap;
				toDelete.AddRange(mergeResult.ToDelete);
			} while (mergeResult.CanMergeAny);

			return new MergeResult(curMap, toDelete, true, false);
		}
		return new MergeResult(addResult.NewMap, new List<PTable>(), false, false);
	}
}
