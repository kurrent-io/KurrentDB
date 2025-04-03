// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteScavengeMapTests : SqliteDbPerTest<SqliteScavengeMapTests> {
	
	[Fact]
	public void throws_on_unsupported_type() {
		Assert.Throws<ArgumentException>(
			() => new SqliteScavengeMap<byte, int>("UnsupportedKeyTypeMap"));
		
		Assert.Throws<ArgumentException>(
			() => new SqliteScavengeMap<int, byte>("UnsupportedValueTypeMap"));
	}
	
	[Fact]
	public void initializes_table_only_once() {
		var sut = new SqliteScavengeMap<int, int>("SomeMap");
		var sqliteBackend = new SqliteBackend(Fixture.DbConnection);
		
		sut.Initialize(sqliteBackend);
		
		sut[33] = 1;
		
		Assert.Null(Record.Exception(() => sut.Initialize(sqliteBackend)));
		Assert.True(sut.TryGetValue(33, out var value));
		Assert.Equal(1, value);
	}
	
	[Fact]
	public void can_use_int_float_map() {
		var sut = new SqliteScavengeMap<int, float>("IntFloatMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[33] = 1;
		sut[1] = 33;
		
		Assert.True(sut.TryGetValue(33, out var v1));
		Assert.Equal(1, v1);
		
		Assert.True(sut.TryGetValue(1, out var v2));
		Assert.Equal(33, v2);
	}

	[Fact]
	public void can_use_string_map() {
		var sut = new SqliteScavengeMap<string, string>("StringMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut["string"] = "string";
		
		Assert.True(sut.TryGetValue("string", out var v));
		Assert.Equal("string", v);
	}
	
	[Fact]
	public void can_overwrite_value() {
		var sut = new SqliteScavengeMap<string, string>("OverwriteStringMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut["string"] = "string-one";
		sut["string"] = "string-two";
		
		Assert.True(sut.TryGetValue("string", out var v));
		Assert.Equal("string-two", v);
	}

	[Fact]
	public void can_store_max_unsigned_long() {
		var sut = new SqliteScavengeMap<ulong, ulong>("UnsignedLongMaxValueMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[ulong.MaxValue] = ulong.MaxValue;
		
		Assert.True(sut.TryGetValue(ulong.MaxValue, out var v));
		Assert.Equal(ulong.MaxValue, v);
	}
	
	[Fact]
	public void can_remove_value_from_map() {
		var sut = new SqliteScavengeMap<int, int>("RemoveValueMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[33] = 1;
		sut[1] = 33;

		Assert.True(sut.TryGetValue(33, out _));
		Assert.True(sut.TryRemove(33, out var removedValue));
		Assert.Equal(1, removedValue);
		Assert.False(sut.TryGetValue(33, out _));
		
		Assert.True(sut.TryGetValue(1, out var v));
		Assert.Equal(33, v);
	}
	
	[Fact]
	public void can_try_remove_value_from_map() {
		var sut = new SqliteScavengeMap<int, int>("TryRemoveValueMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));


		Assert.False(sut.TryRemove(33, out _));
	}
	
	[Fact]
	public void can_get_all_records() {
		var sut = new SqliteScavengeMap<int, int>("EnumerateMap");
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));

		sut[0] = 4;
		sut[1] = 3;
		sut[2] = 2;
		sut[3] = 1;
		sut[4] = 0;
		
		Assert.Collection(sut.AllRecords(),
			item => Assert.Equal(new KeyValuePair<int,int>(0,4), item),
			item => Assert.Equal(new KeyValuePair<int,int>(1,3), item),
			item => Assert.Equal(new KeyValuePair<int,int>(2,2), item),
			item => Assert.Equal(new KeyValuePair<int,int>(3,1), item),
			item => Assert.Equal(new KeyValuePair<int,int>(4,0), item));
	}
}
