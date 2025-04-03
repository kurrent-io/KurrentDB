// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class AdvancingCheckpointTests {
	readonly AdvancingCheckpoint _sut;

	int _readCount;
	long _checkpoint;

	public AdvancingCheckpointTests() {
		_sut = new AdvancingCheckpoint(_ => {
			_readCount++;
			return new(_checkpoint);
		});
	}

	[Fact]
	public async Task compares_correctly() {
		_checkpoint = 4;
		Assert.True(await _sut.IsGreaterThanOrEqualTo(3, CancellationToken.None));
		Assert.True(await _sut.IsGreaterThanOrEqualTo(4, CancellationToken.None));
		Assert.False(await _sut.IsGreaterThanOrEqualTo(5, CancellationToken.None));
		Assert.False(await _sut.IsGreaterThanOrEqualTo(6, CancellationToken.None));

		_checkpoint = 5;
		Assert.True(await _sut.IsGreaterThanOrEqualTo(3, CancellationToken.None));
		Assert.True(await _sut.IsGreaterThanOrEqualTo(4, CancellationToken.None));
		Assert.True(await _sut.IsGreaterThanOrEqualTo(5, CancellationToken.None));
		Assert.False(await _sut.IsGreaterThanOrEqualTo(6, CancellationToken.None));
	}

	[Fact]
	public async Task makes_use_of_cache() {
		_checkpoint = 5;

		Assert.Equal(0, _readCount);

		// initially needs to check the backing store
		Assert.True(await _sut.IsGreaterThanOrEqualTo(4, CancellationToken.None));
		Assert.Equal(1, _readCount);

		// cached
		Assert.True(await _sut.IsGreaterThanOrEqualTo(5, CancellationToken.None));
		Assert.Equal(1, _readCount);

		// check the backing store in case it has advanced
		Assert.False(await _sut.IsGreaterThanOrEqualTo(6, CancellationToken.None));
		Assert.Equal(2, _readCount);

		// check the backing store in case it has advanced
		Assert.False(await _sut.IsGreaterThanOrEqualTo(6, CancellationToken.None));
		Assert.Equal(3, _readCount);
	}

	[Fact]
	public async Task can_reset() {
		_checkpoint = 5;

		Assert.True(await _sut.IsGreaterThanOrEqualTo(4, CancellationToken.None));
		Assert.Equal(1, _readCount);

		_sut.Reset();

		Assert.True(await _sut.IsGreaterThanOrEqualTo(4, CancellationToken.None));
		Assert.Equal(2, _readCount);
	}
}
