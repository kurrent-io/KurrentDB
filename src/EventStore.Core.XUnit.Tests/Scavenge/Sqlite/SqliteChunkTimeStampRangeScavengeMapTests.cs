// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteChunkTimeStampRangeScavengeMapTests : SqliteDbPerTest<SqliteChunkTimeStampRangeScavengeMapTests> {

	[Fact]
	public void can_set_chunk_time_stamp_range() {
		var sut = new SqliteChunkTimeStampRangeScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new ChunkTimeStampRange(Min: DateTime.Now, DateTime.Now.AddDays(1));

		sut[33] = data;

		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v);
	}

	[Fact]
	public void can_overwrite_existing() {
		var sut = new SqliteChunkTimeStampRangeScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[33] = new ChunkTimeStampRange(Min: DateTime.Now, DateTime.Now.AddDays(1));

		var data = new ChunkTimeStampRange(Min: DateTime.Now.AddDays(1), DateTime.Now.AddDays(2));

		sut[33] = data;

		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v);
	}

	[Fact]
	public void can_get_all_records() {
		var sut = new SqliteChunkTimeStampRangeScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetChunkTimeStampRangeTestData();

		sut[0] = osd[0];
		sut[1] = osd[1];
		sut[2] = osd[2];
		sut[3] = osd[3];
		sut[4] = osd[4];

		Assert.Collection(sut.AllRecords(),
			item => {
				Assert.Equal(0, item.Key);
				Assert.Equal(osd[0], item.Value);
			},
			item => {
				Assert.Equal(1, item.Key);
				Assert.Equal(osd[1], item.Value);
			},
			item => {
				Assert.Equal(2, item.Key);
				Assert.Equal(osd[2], item.Value);
			},
			item => {
				Assert.Equal(3, item.Key);
				Assert.Equal(osd[3], item.Value);
			},
			item => {
				Assert.Equal(4, item.Key);
				Assert.Equal(osd[4], item.Value);
			});
	}

	[Fact]
	public void can_remove_value_from_map() {
		var sut = new SqliteChunkTimeStampRangeScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetChunkTimeStampRangeTestData();

		sut[33] = osd[0];
		sut[1] = osd[1];

		Assert.True(sut.TryGetValue(33, out _));
		Assert.True(sut.TryRemove(33, out var removedValue));
		Assert.Equal(osd[0], removedValue);
		Assert.False(sut.TryGetValue(33, out _));

		Assert.True(sut.TryGetValue(1, out var v));
		Assert.Equal(osd[1], v);
	}

	[Fact]
	public void can_try_remove_value_from_map() {
		var sut = new SqliteChunkTimeStampRangeScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		Assert.False(sut.TryRemove(33, out _));
	}

	private ChunkTimeStampRange[] GetChunkTimeStampRangeTestData() {
		return new[] {
			new ChunkTimeStampRange(Min: DateTime.MinValue, DateTime.UtcNow.AddDays(2)),
			new ChunkTimeStampRange(Min: DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(3)),
			new ChunkTimeStampRange(Min: DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(4)),
			new ChunkTimeStampRange(Min: DateTime.UtcNow.AddDays(4), DateTime.UtcNow.AddDays(5)),
			new ChunkTimeStampRange(Min: DateTime.UtcNow.AddDays(5), DateTime.MaxValue)
		};
	}
}
