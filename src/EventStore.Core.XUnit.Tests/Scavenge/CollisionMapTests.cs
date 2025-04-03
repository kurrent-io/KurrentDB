// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Tests.Index.Hashers;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.CollisionManagement;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using EventStore.Core.XUnit.Tests.Scavenge.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

// generally the properties we need of the CollisionManager are tested at a higher
// level. but a couple of fiddly bits are checked in here
public class CollisionMapTests : SqliteDbPerTest<CollisionMapTests> {
	[Fact]
	public void sanity() {
		var collisions = new SqliteCollisionScavengeMap<string>();
		collisions.Initialize(new SqliteBackend(Fixture.DbConnection));
		var sut = GenSut(collisions);

		// add a non-collision for ab-1
		sut["ab-1"] = "foo";

		// value can be retrieved
		Assert.True(sut.TryGetValue("ab-1", out var actual));
		Assert.Equal("foo", actual);

		// "a-1" collides with "a-2".
		collisions["ab-1"] = Unit.Instance;
		collisions["ab-2"] = Unit.Instance;
		sut.NotifyCollision("ab-1");

		// value for "a-1" can still be retrieved
		Assert.True(sut.TryGetValue("ab-1", out actual));
		Assert.Equal("foo", actual);

		// "a-2" not retrieveable since we never set it
		Assert.False(sut.TryGetValue("ab-2", out _));
	}

	private CollisionMap<string, string> GenSut(IScavengeMap<string, Unit> collisions) {
		var nonCollisionsMap = new SqliteScavengeMap<ulong, string>("non_collisions");
		nonCollisionsMap.Initialize(new SqliteBackend(Fixture.DbConnection));

		var collisionsMap = new SqliteScavengeMap<string, string>("collisions");
		collisionsMap.Initialize(new SqliteBackend(Fixture.DbConnection));

		var sut = new CollisionMap<string, string>(
			new HumanReadableHasher(),
			x => collisionsMap.TryGetValue(x, out _),
			nonCollisionsMap,
			collisionsMap);

		return sut;
	}
}

public class OriginalStreamCollisionMapTests : SqliteDbPerTest<OriginalStreamCollisionMapTests> {
	[Fact]
	public void can_enumerate() {
		var collisions = new SqliteCollisionScavengeMap<string>();
		collisions.Initialize(new SqliteBackend(Fixture.DbConnection));
		var sut = GenSut(collisions);

		collisions["ac-1"] = Unit.Instance;
		collisions["ac-2"] = Unit.Instance;
		collisions["ac-3"] = Unit.Instance;

		// non collisions
		sut["ad-4"] = new OriginalStreamData { MaxCount = 4, Status = CalculationStatus.Active };
		sut["ae-5"] = new OriginalStreamData { MaxCount = 5, Status = CalculationStatus.Active };
		sut["af-6"] = new OriginalStreamData { MaxCount = 6, Status = CalculationStatus.Active };
		// collisions
		sut["ac-1"] = new OriginalStreamData { MaxCount = 1, Status = CalculationStatus.Active };
		sut["ac-2"] = new OriginalStreamData { MaxCount = 2, Status = CalculationStatus.Active };
		sut["ac-3"] = new OriginalStreamData { MaxCount = 3, Status = CalculationStatus.Active };

		// no checkpoint
		Assert.Collection(
			sut.EnumerateActive(checkpoint: default),
			x => Assert.Equal("(Id: ac-1, 1)", $"({x.Item1}, {x.Item2.MaxCount})"),
			x => Assert.Equal("(Id: ac-2, 2)", $"({x.Item1}, {x.Item2.MaxCount})"),
			x => Assert.Equal("(Id: ac-3, 3)", $"({x.Item1}, {x.Item2.MaxCount})"),
			x => Assert.Equal("(Hash: 100, 4)", $"({x.Item1}, {x.Item2.MaxCount})"),
			x => Assert.Equal("(Hash: 101, 5)", $"({x.Item1}, {x.Item2.MaxCount})"),
			x => Assert.Equal("(Hash: 102, 6)", $"({x.Item1}, {x.Item2.MaxCount})"));

		// id checkpoint
		Assert.Collection(
			sut.EnumerateActive(checkpoint: StreamHandle.ForStreamId("ac-2")),
			x => Assert.Equal("(Id: ac-3, 3)", $"({x.Item1}, {x.Item2.MaxCount})"),
			x => Assert.Equal("(Hash: 100, 4)", $"({x.Item1}, {x.Item2.MaxCount})"),
			x => Assert.Equal("(Hash: 101, 5)", $"({x.Item1}, {x.Item2.MaxCount})"),
			x => Assert.Equal("(Hash: 102, 6)", $"({x.Item1}, {x.Item2.MaxCount})"));

		// hash checkpoint
		Assert.Collection(
			sut.EnumerateActive(checkpoint: StreamHandle.ForHash<string>(101)),
			x => Assert.Equal("(Hash: 102, 6)", $"({x.Item1}, {x.Item2.MaxCount})"));

		// end checkpoint
		Assert.Empty(sut.EnumerateActive(checkpoint: StreamHandle.ForHash<string>(102)));
	}

	private OriginalStreamCollisionMap<string> GenSut(IScavengeMap<string, Unit> collisions) {
		var nonCollisionsMap = new SqliteOriginalStreamScavengeMap<ulong>("non_collisions");
		nonCollisionsMap.Initialize(new SqliteBackend(Fixture.DbConnection));

		var collisionsMap = new SqliteOriginalStreamScavengeMap<string>("collisions");
		collisionsMap.Initialize(new SqliteBackend(Fixture.DbConnection));

		var sut = new OriginalStreamCollisionMap<string>(
			new HumanReadableHasher(),
			x => collisions.TryGetValue(x, out _),
			nonCollisionsMap,
			collisionsMap);

		return sut;
	}
}
