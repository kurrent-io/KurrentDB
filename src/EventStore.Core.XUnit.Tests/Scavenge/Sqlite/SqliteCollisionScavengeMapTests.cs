// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteCollisionScavengeMapTests : SqliteDbPerTest<SqliteCollisionScavengeMapTests> {
	
	[Fact]
	public void can_set_value() {
		var sut = new SqliteCollisionScavengeMap<int>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		
		sut[33] = Unit.Instance;

		Assert.True(sut.TryGetValue(33, out var v));
		Assert.Equal(Unit.Instance, v);
	}
	
	[Fact]
	public void can_overwrite_value() {
		var sut = new SqliteCollisionScavengeMap<string>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut["33"] = Unit.Instance;
		sut["33"] = Unit.Instance;

		Assert.True(sut.TryGetValue("33", out var v));
		Assert.Equal(Unit.Instance, v);
	}
	
	[Fact]
	public void can_get_all_records() {
		var sut = new SqliteCollisionScavengeMap<int>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[0] = Unit.Instance;
		sut[1] = Unit.Instance;
		sut[2] = Unit.Instance;
		sut[3] = Unit.Instance;
		sut[4] = Unit.Instance;
		
		Assert.Collection(sut.AllRecords(),
			item => Assert.Equal(new KeyValuePair<int,Unit>(0, Unit.Instance), item),
			item => Assert.Equal(new KeyValuePair<int,Unit>(1, Unit.Instance), item),
			item => Assert.Equal(new KeyValuePair<int,Unit>(2, Unit.Instance), item),
			item => Assert.Equal(new KeyValuePair<int,Unit>(3, Unit.Instance), item),
			item => Assert.Equal(new KeyValuePair<int,Unit>(4, Unit.Instance), item));
	}

	[Fact]
	public void can_try_get_value_of_non_existing() {
		var sut = new SqliteCollisionScavengeMap<int>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		Assert.False(sut.TryGetValue(33, out var v));
		Assert.Equal(default, v);
	}
	
	[Fact]
	public void can_remove_value() {
		var sut = new SqliteCollisionScavengeMap<int>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[0] = Unit.Instance;
		
		Assert.True(sut.TryRemove(0, out var v));
		Assert.Equal(Unit.Instance, v);
	}
	
	[Fact]
	public void can_try_remove_value() {
		var sut = new SqliteCollisionScavengeMap<string>();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		Assert.False(sut.TryRemove("0", out var v));
		Assert.Equal(default, v);
	}
}
