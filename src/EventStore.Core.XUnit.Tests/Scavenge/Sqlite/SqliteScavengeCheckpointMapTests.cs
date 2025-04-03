// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteScavengeCheckpointMapTests : SqliteDbPerTest<SqliteScavengeCheckpointMapTests> {

	[Fact]
	public void can_store_checkpoint() {
		var sut = new SqliteScavengeCheckpointMap<int>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var scavengePoint = new ScavengePoint(
			position: 303,
			eventNumber: 5,
			effectiveNow: DateTime.UtcNow,
			threshold: 6);
		sut[Unit.Instance] = new ScavengeCheckpoint.Accumulating(scavengePoint, 22);

		Assert.True(sut.TryGetValue(Unit.Instance, out var v));
		Assert.NotNull(v);
		Assert.IsType<ScavengeCheckpoint.Accumulating>(v);
		Assert.Equal(22, ((ScavengeCheckpoint.Accumulating)v).DoneLogicalChunkNumber);
		Assert.Equal(scavengePoint.Position, v.ScavengePoint.Position);
		Assert.Equal(scavengePoint.EventNumber, v.ScavengePoint.EventNumber);
		Assert.Equal(scavengePoint.EffectiveNow, v.ScavengePoint.EffectiveNow);
		Assert.Equal(scavengePoint.Threshold, v.ScavengePoint.Threshold);
	}

	[Fact]
	public void can_overwrite_current_checkpoint() {
		var sut = new SqliteScavengeCheckpointMap<int>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[Unit.Instance] = new ScavengeCheckpoint.Accumulating(
			new ScavengePoint(
				position: 303,
				eventNumber: 5,
				effectiveNow: DateTime.UtcNow.AddHours(-1),
				threshold: 6),
			22);

		var scavengePoint = new ScavengePoint(
			position: 909,
			eventNumber: 6,
			effectiveNow: DateTime.UtcNow,
			threshold: 7);

		sut[Unit.Instance] = new ScavengeCheckpoint.ExecutingChunks(scavengePoint, 43);
		
		Assert.True(sut.TryGetValue(Unit.Instance, out var v));
		Assert.NotNull(v);
		Assert.IsType<ScavengeCheckpoint.ExecutingChunks>(v);
		Assert.Equal(43, ((ScavengeCheckpoint.ExecutingChunks)v).DoneLogicalChunkNumber);
		Assert.Equal(scavengePoint.Position, v.ScavengePoint.Position);
		Assert.Equal(scavengePoint.EventNumber, v.ScavengePoint.EventNumber);
		Assert.Equal(scavengePoint.EffectiveNow, v.ScavengePoint.EffectiveNow);
		Assert.Equal(scavengePoint.Threshold, v.ScavengePoint.Threshold);
	}

	[Fact]
	public void can_remove_current_checkpoint() {
		var sut = new SqliteScavengeCheckpointMap<int>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var scavengePoint = new ScavengePoint(
			position: 303,
			eventNumber: 5,
			effectiveNow: DateTime.UtcNow,
			threshold: 6);
		sut[Unit.Instance] = new ScavengeCheckpoint.Accumulating(scavengePoint, 22);

		Assert.True(sut.TryRemove(Unit.Instance, out var v));
		Assert.NotNull(v);
		Assert.IsType<ScavengeCheckpoint.Accumulating>(v);
		Assert.Equal(scavengePoint.Position, v.ScavengePoint.Position);
		Assert.Equal(scavengePoint.EventNumber, v.ScavengePoint.EventNumber);
		Assert.Equal(scavengePoint.EffectiveNow, v.ScavengePoint.EffectiveNow);
		Assert.Equal(scavengePoint.Threshold, v.ScavengePoint.Threshold);
	}

	[Fact]
	public void can_try_remove_checkpoint() {
		var sut = new SqliteScavengeCheckpointMap<int>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		Assert.False(sut.TryRemove(Unit.Instance, out _));
	}
}
