// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogV2;
using EventStore.Core.Tests.Index.Hashers;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

// generally the properties we need of the ScavengeState are tested at a higher
// level. but a couple of fiddly bits are checked in here
public class ScavengeStateTests : SqliteDbPerTest<ScavengeStateTests> {
	[Fact]
	public void pending_changes_are_still_read() {
		var hasher = new HumanReadableHasher();
		var metastreamLookup = new LogV2SystemStreams();
		var sut = new ScavengeStateBuilder<string>(hasher, metastreamLookup)
			.WithConnectionPool(Fixture.DbConnectionPool)
			.Build();

		var trans = sut.BeginTransaction();

		sut.IncreaseChunkWeight(5, 20);
		sut.IncreaseChunkWeight(6, 40);
		sut.SetMetastreamDiscardPoint("$$ab-1", DiscardPoint.DiscardBefore(20));

		Assert.Equal(60, sut.SumChunkWeights(5, 6));
		Assert.True(sut.TryGetMetastreamData("$$ab-1", out var actual));
		Assert.Equal(DiscardPoint.DiscardBefore(20), actual.DiscardPoint);
		trans.Commit(new ScavengeCheckpoint.Accumulating(
			new ScavengePoint(default, default, default, default),
			20));
		sut.Dispose();
	}

	[Fact]
	public void transaction_rollback_undoes_pending_changes() {
		var hasher = new HumanReadableHasher();
		var metastreamLookup = new LogV2SystemStreams();
		var sut = new ScavengeStateBuilder<string>(hasher, metastreamLookup)
			.WithConnectionPool(Fixture.DbConnectionPool)
			.Build();

		sut.IncreaseChunkWeight(5, 20);
		sut.DetectCollisions("$$ab-1");
		sut.SetMetastreamDiscardPoint("$$ab-1", DiscardPoint.DiscardBefore(20));

		var trans = sut.BeginTransaction();

		sut.IncreaseChunkWeight(6, 40);
		sut.SetMetastreamDiscardPoint("$$ab-1", DiscardPoint.DiscardBefore(50));
		sut.DetectCollisions("$$ab-2");
		sut.SetMetastreamDiscardPoint("$$ab-2", DiscardPoint.DiscardBefore(50));
		sut.DetectCollisions("$$cd-3");
		sut.SetMetastreamDiscardPoint("$$cd-3", DiscardPoint.DiscardBefore(50));

		trans.Rollback();

		Assert.Equal(20, sut.SumChunkWeights(5, 6));
		Assert.True(sut.TryGetMetastreamData("$$ab-1", out var actual));
		Assert.Equal(DiscardPoint.DiscardBefore(20), actual.DiscardPoint);
		sut.DetectCollisions("$$ab-2");
		Assert.False(sut.TryGetMetastreamData("$$ab-2", out _));
		sut.DetectCollisions("$$cd-3");
		Assert.False(sut.TryGetMetastreamData("$$cd-3", out _));
		sut.Dispose();
	}

	[Fact]
	public void active_iteration_no_checkpoint() {
		var hasher = new CompositeHasher<string>(
			new XXHashUnsafe(),
			new Murmur3AUnsafe());

		var metastreamLookup = new LogV2SystemStreams();
		var sut = new ScavengeStateBuilder<string>(hasher, metastreamLookup)
			.WithConnectionPool(Fixture.DbConnectionPool)
			.Build();

		var numStreams = 100;

		// 'accumulate'
		for (var i = 0; i < numStreams; i++) {
			var streamId = $"s-{i}";
			var metastreamId = $"$$s-{i}";
			sut.DetectCollisions(streamId);
			sut.DetectCollisions(metastreamId);
			sut.SetOriginalStreamMetadata(streamId, metadata: new StreamMetadata(maxCount: i + 1));
		}

		Assert.Empty(sut.AllCollisions());

		// 'calculate'
		var processed = 0;
		long? lastHash = null;

		var xs = sut.OriginalStreamsToCalculate(default);
		foreach (var (handle, data) in xs) {
			processed++;
			Assert.Equal(StreamHandle.Kind.Hash, handle.Kind);
			if (lastHash >= (long)handle.StreamHash) {
				throw new Exception("repeated");
			}
			lastHash = (long)handle.StreamHash;

			sut.SetOriginalStreamDiscardPoints(
				handle,
				CalculationStatus.Spent,
				DiscardPoint.KeepAll,
				DiscardPoint.KeepAll);
		}

		// processedd each stream exactly once
		Assert.Equal(numStreams, processed);
		sut.Dispose();
	}

	[Fact]
	public void active_iteration_with_checkpoint() {
		var hasher = new CompositeHasher<string>(
			new XXHashUnsafe(),
			new Murmur3AUnsafe());

		var metastreamLookup = new LogV2SystemStreams();
		var sut = new ScavengeStateBuilder<string>(hasher, metastreamLookup)
			.WithConnectionPool(Fixture.DbConnectionPool)
			.Build();

		var numStreams = 100;
		var stopAfter = 50;

		// 'accumulate'
		for (var i = 0; i < numStreams; i++) {
			var streamId = $"s-{i}";
			var metastreamId = $"$$s-{i}";
			sut.DetectCollisions(streamId);
			sut.DetectCollisions(metastreamId);
			sut.SetOriginalStreamMetadata(streamId, metadata: new StreamMetadata(maxCount: i + 1));
		}

		Assert.Empty(sut.AllCollisions());

		// 'calculate'
		var processed = 0;
		var checkpoint = new StreamHandle<string>();
		long? lastHash = null;

		void Calculate(IEnumerable<(StreamHandle<string>, OriginalStreamData)> xs) {
			foreach (var (handle, data) in xs) {
				processed++;
				Assert.Equal(StreamHandle.Kind.Hash, handle.Kind);
				if (lastHash >= (long)handle.StreamHash) {
					throw new Exception("repeated");
				}
				lastHash = (long)handle.StreamHash;

				sut.SetOriginalStreamDiscardPoints(
					handle,
					CalculationStatus.Spent,
					DiscardPoint.KeepAll,
					DiscardPoint.KeepAll);

				checkpoint = handle;

				if (processed == stopAfter)
					break;
			}
		}

		Calculate(sut.OriginalStreamsToCalculate(checkpoint));
		Assert.Equal(50, processed);
		Calculate(sut.OriginalStreamsToCalculate(checkpoint));

		// processedd each stream exactly once
		Assert.Equal(numStreams, processed);
		sut.Dispose();
	}
}
