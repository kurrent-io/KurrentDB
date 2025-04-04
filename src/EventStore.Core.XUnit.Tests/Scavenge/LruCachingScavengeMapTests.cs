// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.InMemory;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class LruCachingScavengeMapTests {
	[Fact]
	public void writing_writes_through() {
		var wrapped = new InMemoryScavengeMap<string, int>();
		var sut = new LruCachingScavengeMap<string, int>("", wrapped, cacheMaxCount: 100);

		// given empty wrapped

		// when we write
		sut["3"] = 3;

		// then it writes through
		Assert.True(wrapped.TryGetValue("3", out var v));
		Assert.Equal(3, v);
	}

	[Fact]
	public void reading_reads_through() {
		var wrapped = new InMemoryScavengeMap<string, int>();
		var sut = new LruCachingScavengeMap<string, int>("", wrapped, cacheMaxCount: 100);

		// given some data in the wrapped
		wrapped["3"] = 3;

		// then we can read that data
		Assert.True(sut.TryGetValue("3", out var v));
		Assert.Equal(3, v);
	}

	[Fact]
	public void reading_populates_cache() {
		var wrapped = new InMemoryScavengeMap<string, int>();
		var sut = new LruCachingScavengeMap<string, int>("", wrapped, cacheMaxCount: 100);

		// given some data in the wrapped
		wrapped["3"] = 3;

		// when we read that data
		sut.TryGetValue("3", out _);

		// then subsequent read comes from cache
		wrapped["3"] = 4;
		Assert.True(sut.TryGetValue("3", out var v));
		Assert.Equal(3, v);
	}

	[Fact]
	public void writing_populates_cache() {
		var wrapped = new InMemoryScavengeMap<string, int>();
		var sut = new LruCachingScavengeMap<string, int>("", wrapped, cacheMaxCount: 100);

		// given empty wrapped

		// when we write
		sut["3"] = 3;

		// then subsequent read comes from cache
		wrapped["3"] = 4;
		Assert.True(sut.TryGetValue("3", out var v));
		Assert.Equal(3, v);
	}

	[Fact]
	public void can_tryget_non_existent() {
		var wrapped = new InMemoryScavengeMap<string, int>();
		var sut = new LruCachingScavengeMap<string, int>("", wrapped, cacheMaxCount: 100);

		// given empty wrapped

		// then reading returns false
		Assert.False(sut.TryGetValue("3", out var _));
	}

	[Fact]
	public void can_tryremove_uncached() {
		var wrapped = new InMemoryScavengeMap<string, int>();
		var sut = new LruCachingScavengeMap<string, int>("", wrapped, cacheMaxCount: 100);

		// given some data in the wrapped
		wrapped["3"] = 3;

		// then we can remove it
		Assert.True(sut.TryRemove("3", out var v));
		Assert.Equal(3, v);

		// and can no longer find it
		Assert.False(sut.TryGetValue("3", out _));
		Assert.False(wrapped.TryGetValue("3", out _));
	}

	[Fact]
	public void can_tryremove_cached() {
		var wrapped = new InMemoryScavengeMap<string, int>();
		var sut = new LruCachingScavengeMap<string, int>("", wrapped, cacheMaxCount: 100);

		// given some data in the wrapped
		wrapped["3"] = 3;

		// when we read it
		sut.TryGetValue("3", out _);

		// then we can remove it
		Assert.True(sut.TryRemove("3", out var v));
		Assert.Equal(3, v);

		// and can no longer find it
		Assert.False(sut.TryGetValue("3", out _));
		Assert.False(wrapped.TryGetValue("3", out _));
	}

	[Fact]
	public void can_tryremove_non_existent() {
		var wrapped = new InMemoryScavengeMap<string, int>();
		var sut = new LruCachingScavengeMap<string, int>("", wrapped, cacheMaxCount: 100);

		// given empty wrapped

		// then removing returns false
		Assert.False(sut.TryRemove("3", out var _));
	}
}
