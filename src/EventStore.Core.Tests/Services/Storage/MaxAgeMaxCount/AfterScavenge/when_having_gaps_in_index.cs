// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.MaxAgeMaxCount.AfterScavenge;

public abstract class MaxAgeIterationTests : ReadIndexTestScenario<LogFormat.V2, string> {
	public ulong _esHash;

	protected abstract long[] ExtantIndexEntries { get; }

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var now = DateTime.UtcNow;
		var expired = now.AddMinutes(-50);

		var metadata = string.Format(@"{{""$maxAge"":{0}}}", (int)TimeSpan.FromMinutes(10).TotalSeconds);

		await WriteStreamMetadata("ES", 0, metadata, token: token);

		// the stream had ten events in it but 8 of them have expired leaving only the last two.
		await WriteSingleEvent("ES", 0, "data", expired, token: token);
		await WriteSingleEvent("ES", 1, "data", expired, token: token);
		await WriteSingleEvent("ES", 2, "data", expired, token: token);
		await WriteSingleEvent("ES", 3, "data", expired, token: token);
		await WriteSingleEvent("ES", 4, "data", expired, token: token);
		await WriteSingleEvent("ES", 5, "data", expired, token: token);
		await WriteSingleEvent("ES", 6, "data", expired, token: token);
		await WriteSingleEvent("ES", 7, "data", expired, token: token);
		await WriteSingleEvent("ES", 8, "data", now, token: token);
		await WriteSingleEvent("ES", 9, "data", now, token: token);
		_esHash = Hasher.Hash("ES");

		// dont scavenge the index because we want to keep most of the entries
		// simulate removal by using FilteredTableIndex
		Scavenge(completeLast: true, mergeChunks: false, scavengeIndex: false);
	}

	protected override ITableIndex<string> TransformTableIndex(ITableIndex<string> tableIndex) {
		return new FilteredTableIndex<string>(
			tableIndex,
			// keep everything from other streams, but only the ExtantEntries of "ES"
			entry => entry.Stream != _esHash || ExtantIndexEntries.Contains(entry.Version));
	}

	// repeatedly issue reads, each one starting from the previous `nextEventNumber`
	// until we find the event we are looking for.
	protected async ValueTask ReadOneStartingFrom(long fromEventNumber, long expectedEventNumber, CancellationToken token) {
		for (int i = 0; i < 20; i++) {
			var result = await ReadIndex.ReadStreamEventsForward("ES", fromEventNumber, maxCount: 1, token);

			if (result.Records.Length != 0) {
				Assert.AreEqual(expectedEventNumber, result.Records[0].EventNumber);
				return;
			}

			fromEventNumber = result.NextEventNumber;
		}

		throw new Exception("iterated too many times. infinite loop?");
	}

	protected async ValueTask ReadOneStartingFromEach(CancellationToken token = default) {
		await ReadOneStartingFrom(0, 8, token);
		await ReadOneStartingFrom(1, 8, token);
		await ReadOneStartingFrom(2, 8, token);
		await ReadOneStartingFrom(3, 8, token);
		await ReadOneStartingFrom(4, 8, token);
		await ReadOneStartingFrom(5, 8, token);
		await ReadOneStartingFrom(6, 8, token);
		await ReadOneStartingFrom(7, 8, token);
		await ReadOneStartingFrom(8, 8, token);
		await ReadOneStartingFrom(9, 9, token);
	}
}

public class when_having_gaps_in_index_on_maxage_fast_path : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 0, 1, 2, 3, 4, 5, 6, 8, 9 };

	[Test]
	public async Task works() => await ReadOneStartingFromEach();
}

public class when_having_gaps_in_index_on_maxage_fast_path2 : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 0, 8, 9 };

	[Test]
	public async Task works() => await ReadOneStartingFromEach();
}

public class when_having_gaps_in_index_on_maxage_fast_path3 : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 2, 8, 9 };

	[Test]
	public async Task works() => await ReadOneStartingFromEach();
}

public class when_having_gaps_in_index_on_maxage_fast_path4 : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 1, 3, 5, 7, 8, 9 };

	[Test]
	public async Task works() => await ReadOneStartingFromEach();
}

public class when_having_gaps_in_index_on_maxage_fast_path5 : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 0, 2, 4, 6, 8, 9 };

	[Test]
	public async Task works() => await ReadOneStartingFromEach();
}
