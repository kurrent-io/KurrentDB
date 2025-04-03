// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteMetastreamScavengeMapTests : SqliteDbPerTest<SqliteMetastreamScavengeMapTests> {
	
	[Fact]
	public void can_set_metastream_data() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("SetData");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new MetastreamData(isTombstoned: true, DiscardPoint.DiscardIncluding(5));

		sut[33] = data;

		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v);
	}
	
	[Fact]
	public void can_overwrite_existing() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("Overwrite");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[33] = new MetastreamData(isTombstoned: true, DiscardPoint.DiscardIncluding(5));
		
		var data = new MetastreamData(isTombstoned: false, DiscardPoint.DiscardIncluding(15));

		sut[33] = data;
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v);
	}

	[Fact]
	public void can_set_tombstone_of_existing() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("TombstoneSetExisting");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new MetastreamData(isTombstoned: false, DiscardPoint.DiscardIncluding(5));

		sut[33] = data;
		
		sut.SetTombstone(33);
		data = new MetastreamData(isTombstoned: true, data.DiscardPoint);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data, v);
	}
	
	[Fact]
	public void can_set_tombstone_of_non_existing() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("TombstoneSetNonExisting");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut.SetTombstone(33);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(new MetastreamData(isTombstoned: true, DiscardPoint.KeepAll), v);
	}
	
	[Fact]
	public void can_set_discard_point_of_existing() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("DiscardPointExisting");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var data = new MetastreamData(isTombstoned: false, DiscardPoint.DiscardIncluding(5));
		sut[33] = data;

		var discardPoint = DiscardPoint.DiscardIncluding(55);
		sut.SetDiscardPoint(33, discardPoint);
		
		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(data.IsTombstoned, v.IsTombstoned);
		Assert.Equal(discardPoint, v.DiscardPoint);
	}
	
	[Fact]
	public void can_set_discard_point_of_non_existing() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("DiscardPointNonExisting");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var discardPoint = DiscardPoint.DiscardIncluding(12);
		sut.SetDiscardPoint(33, discardPoint);

		Assert.True(sut.TryGetValue(33, out var v));
		Assert.False(v.IsTombstoned);
		Assert.Equal(discardPoint, v.DiscardPoint);
	}
	
	[Fact]
	public void can_get_all_records() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("EnumerateAll");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetMetastreamTestData();
		
		sut[0] = osd[0];
		sut[1] = osd[1];
		sut[2] = osd[2];
		sut[3] = osd[3];
		sut[4] = osd[4];

		Assert.Collection(sut.AllRecords(),
			item => {
				Assert.Equal(0ul, item.Key);
				Assert.Equal(osd[0], item.Value);
			},
			item => {
				Assert.Equal(1ul, item.Key);
				Assert.Equal(osd[1], item.Value);
			},
			item => {
				Assert.Equal(2ul, item.Key);
				Assert.Equal(osd[2], item.Value);
			},
			item => {
				Assert.Equal(3ul, item.Key);
				Assert.Equal(osd[3], item.Value);
			},
			item => {
				Assert.Equal(4ul, item.Key);
				Assert.Equal(osd[4], item.Value);
			});
	}

	[Fact]
	public void can_remove_value_from_map() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("Remove");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetMetastreamTestData();
		
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
		var sut = new SqliteMetastreamScavengeMap<ulong>("OriginalStreamScavengeMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		Assert.False(sut.TryRemove(33, out _));
	}
	
	[Fact]
	public void can_remove_all() {
		var sut = new SqliteMetastreamScavengeMap<ulong>("EnumerateAll");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		var osd = GetMetastreamTestData();
		
		sut[0] = osd[0];
		sut[1] = osd[1];
		sut[2] = osd[2];
		sut[3] = osd[3];
		sut[4] = osd[4];

		sut.DeleteAll();

		Assert.Empty(sut.AllRecords());
	}

	private MetastreamData[] GetMetastreamTestData() {
		return new[] {
			new MetastreamData(isTombstoned: false, DiscardPoint.DiscardIncluding(5)),
			new MetastreamData(isTombstoned: false, DiscardPoint.DiscardIncluding(50)),
			new MetastreamData(isTombstoned: true, DiscardPoint.DiscardIncluding(15)),
			new MetastreamData(isTombstoned: false, DiscardPoint.DiscardIncluding(25)),
			new MetastreamData(isTombstoned: true, DiscardPoint.DiscardIncluding(53))
		};
	}
}
