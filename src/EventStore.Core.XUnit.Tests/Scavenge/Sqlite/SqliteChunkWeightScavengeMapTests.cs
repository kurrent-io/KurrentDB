// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteChunkWeightScavengeMapTests : SqliteDbPerTest<SqliteChunkWeightScavengeMapTests> {

	[Fact]
	public void can_increase_existing_chunk_weight() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		
		sut[3] = 0.5f;
		
		sut.IncreaseWeight(3, 0.1f);

		Assert.True(sut.TryGetValue(3, out var v1));
		Assert.Equal(0.6f, v1);
	}
	
	[Fact]
	public void can_decrease_existing_chunk_weight() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		
		sut[3] = 0.5f;
		
		sut.IncreaseWeight(3, -0.1f);

		Assert.True(sut.TryGetValue(3, out var v));
		Assert.Equal(0.4f, v);
	}
	
	[Fact]
	public void can_increase_non_existing_chunk_weight() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		
		sut.IncreaseWeight(13, 0.33f);

		Assert.True(sut.TryGetValue(13, out var v));
		Assert.Equal(0.33f, v);
	}

	[Fact]
	public void can_see_if_all_weights_zero_false() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		sut[2] = 0.1f;
		sut[4] = -0.1f;
		Assert.False(sut.AllWeightsAreZero());
	}

	[Fact]
	public void can_see_if_all_weights_zero_true() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		sut[2] = 0.0f;
		Assert.True(sut.AllWeightsAreZero());
	}

	[Fact]
	public void can_see_if_all_weights_zero_true_empty() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		Assert.True(sut.AllWeightsAreZero());
	}

	[Fact]
	public void can_sum_chunk_weights() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		
		sut[0] = 0.1f;
		sut[1] = 0.1f;
		sut[2] = 0.1f;
		sut[3] = 0.1f;
		sut[4] = 0.1f;

		var value = sut.SumChunkWeights(1, 3);
		
		Assert.Equal(0.3f, value);
	}
	
	[Fact]
	public void can_sum_non_existing_chunk_weights() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		
		var value = sut.SumChunkWeights(1, 3);
		
		Assert.Equal(0.0f, value);
	}
	
	[Fact]
	public void can_reset_chunk_weights() {
		var sut = new SqliteChunkWeightScavengeMap();
		sut.Initialize(new SqliteBackend(Fixture.DbConnection));
		
		sut[0] = 0.1f;
		sut[1] = 0.1f;
		sut[2] = 0.1f;
		sut[3] = 0.1f;
		sut[4] = 0.1f;

		sut.ResetChunkWeights(1, 3);
		
		Assert.Collection(sut.AllRecords(),
			item => Assert.Equal(new KeyValuePair<int,float>(0, 0.1f), item),
			item => Assert.Equal(new KeyValuePair<int,float>(4, 0.1f), item));
	}
}
