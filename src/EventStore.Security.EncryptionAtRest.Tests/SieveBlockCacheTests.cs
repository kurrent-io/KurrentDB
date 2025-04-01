// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Security.EncryptionAtRest.Transforms.DataStructures;

namespace EventStore.Security.EncryptionAtRest.Tests;

public class SieveBlockCacheTests {
	private const int DataSize = 10;

	[Fact]
	public void works() {
		// example from: https://cachemon.github.io/SIEVE-website/blog/assets/sieve/sieve_diagram_animation.gif
		var sut = new SieveBlockCache(7, DataSize);
		var data = new byte[DataSize];

		// set up
		Put('A');
		Put('B');
		Put('C');
		Put('D');
		Put('E');
		Put('F');
		Put('G');
		Get('A');
		Get('B');
		Get('G');

		// tests
		Put('H');
		CantGet('C');

		Put('A');
		Put('D');
		Put('I');
		CantGet('E');

		Put('B');
		Put('J');
		CantGet('F');

		// final checks
		CanGet('A');
		CanGet('B');
		CanGet('D');
		CanGet('G');
		CanGet('H');
		CanGet('I');
		CanGet('J');

		return;

		bool Get(char c) {
			return sut.TryGet(c, data);
		}

		void Put(char c) {
			if (!Get(c))
				sut.Put(c, GenData(c));
		}

		void CanGet(char c) {
			Assert.True(Get(c));
			Assert.True(data.All(x => x == c));
		}

		void CantGet(char c) {
			Assert.False(Get(c));
		}

		static ReadOnlySpan<byte> GenData(char c) {
			var data = new byte[DataSize];
			for (var i = 0; i < DataSize; i++)
				data[i] = (byte) c;
			return data;
		}
	}

	[Fact]
	public void works_with_cache_size_one() {
		var sut = new SieveBlockCache(1, 0);
		var data = Array.Empty<byte>();

		sut.Put('A', data);
		Assert.True(sut.TryGet('A', data));

		sut.Put('B', data);
		Assert.True(sut.TryGet('B', data));

		sut.Put('C', data);
		Assert.True(sut.TryGet('C', data));

		sut.Put('D', data);
		Assert.True(sut.TryGet('D', data));

		Assert.False(sut.TryGet('A', data));
		Assert.False(sut.TryGet('B', data));
		Assert.False(sut.TryGet('C', data));
	}

	[Fact]
	public void throws_with_cache_size_zero() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => new SieveBlockCache(0, 1));

	[Fact]
	public void throws_with_negative_block_data_size() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => new SieveBlockCache(1, -1));
}
