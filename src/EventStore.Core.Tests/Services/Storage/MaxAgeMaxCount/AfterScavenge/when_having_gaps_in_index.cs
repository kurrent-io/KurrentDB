// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.MaxAgeMaxCount.AfterScavenge;

public abstract class MaxAgeIterationTests : ReadIndexTestScenario<LogFormat.V2, string> {
	public ulong _esHash;

	protected abstract long[] ExtantIndexEntries { get; }

	protected override void WriteTestScenario() {
		var now = DateTime.UtcNow;
		var expired = now.AddMinutes(-50);

		var metadata = string.Format(@"{{""$maxAge"":{0}}}", (int)TimeSpan.FromMinutes(10).TotalSeconds);

		WriteStreamMetadata("ES", 0, metadata);

		// the stream had ten events in it but 8 of them have expired leaving only the last two.
		WriteSingleEvent("ES", 0, "data", expired);
		WriteSingleEvent("ES", 1, "data", expired);
		WriteSingleEvent("ES", 2, "data", expired);
		WriteSingleEvent("ES", 3, "data", expired);
		WriteSingleEvent("ES", 4, "data", expired);
		WriteSingleEvent("ES", 5, "data", expired);
		WriteSingleEvent("ES", 6, "data", expired);
		WriteSingleEvent("ES", 7, "data", expired);
		WriteSingleEvent("ES", 8, "data", now);
		WriteSingleEvent("ES", 9, "data", now);
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
	protected void ReadOneStartingFrom(long fromEventNumber, long expectedEventNumber) {
		var attempts = new List<long>();
		for (int i = 0; i < 20; i++) {
			attempts.Add(fromEventNumber);
			var result = ReadIndex.ReadStreamEventsForward("ES", fromEventNumber, maxCount: 1);

			if (result.Records.Length != 0) {
				Assert.AreEqual(expectedEventNumber, result.Records[0].EventNumber);
				return;
			}

			fromEventNumber = result.NextEventNumber;
		}

		throw new Exception("iterated too many times. infinite loop?");
	}

	protected void ReadOneStartingFromEach() {
		ReadOneStartingFrom(0, 8);
		ReadOneStartingFrom(1, 8);
		ReadOneStartingFrom(2, 8);
		ReadOneStartingFrom(3, 8);
		ReadOneStartingFrom(4, 8);
		ReadOneStartingFrom(5, 8);
		ReadOneStartingFrom(6, 8);
		ReadOneStartingFrom(7, 8);
		ReadOneStartingFrom(8, 8);
		ReadOneStartingFrom(9, 9);
	}
}

public class when_having_gaps_in_index_on_maxage_fast_path : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 0, 1, 2, 3, 4, 5, 6, 8, 9 };

	[Test]
	public void works() => ReadOneStartingFromEach();
}

public class when_having_gaps_in_index_on_maxage_fast_path2 : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 0, 8, 9 };

	[Test]
	public void works() => ReadOneStartingFromEach();
}

public class when_having_gaps_in_index_on_maxage_fast_path3 : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 2, 8, 9 };

	[Test]
	public void works() => ReadOneStartingFromEach();
}

public class when_having_gaps_in_index_on_maxage_fast_path4 : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 1, 3, 5, 7, 8, 9 };

	[Test]
	public void works() => ReadOneStartingFromEach();
}

public class when_having_gaps_in_index_on_maxage_fast_path5 : MaxAgeIterationTests {
	protected override long[] ExtantIndexEntries { get; } = new long[] { 0, 2, 4, 6, 8, 9 };

	[Test]
	public void works() => ReadOneStartingFromEach();
}
