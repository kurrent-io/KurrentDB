// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.DataStructures.ProbabilisticFilter;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.DataStructures.ProbabilisticFilter;

public abstract class PersistenceStrategyTests : DirectoryPerTest<PersistenceStrategyTests> {
	protected abstract IPersistenceStrategy GenSut(
		long size, bool create, string fileName);

	protected IPersistenceStrategy CreateSut(long size, string fileName = "thefilter") {
		var sut = GenSut(size, create: true, fileName);
		sut.Init();
		sut.WriteHeader(new Header { NumBits = size * 8, Version = Header.CurrentVersion });
		return sut;
	}

	protected IPersistenceStrategy OpenSut(long size, string fileName = "thefilter") {
		var sut = GenSut(size, create: false, fileName);
		sut.Init();
		var header = sut.ReadHeader();
		Assert.Equal(size * 8, header.NumBits);
		return sut;
	}

	[Fact]
	public void AfterInitializationIsFilledWithZeroes() {
		using (var sut = CreateSut(10_000)) {
			for (var bitPosition = 0; bitPosition < 10_000 * 8; bitPosition++) {
				Assert.False(sut.DataAccessor.IsBitSet(bitPosition));
			}

			sut.Flush();
		}

		// and after reopening
		using (var sut = OpenSut(10_000)) {
			for (var bitPosition = 0; bitPosition < 10_000 * 8; bitPosition++) {
				Assert.False(sut.DataAccessor.IsBitSet(bitPosition));
			}
		}
	}

	[Fact]
	public void FlushesEntireLogicalFilter() {
		using (var sut = CreateSut(10_000)) {
			for (var bitPosition = 0; bitPosition < 10_000 * 8; bitPosition++) {
				Assert.False(sut.DataAccessor.IsBitSet(bitPosition));
				sut.DataAccessor.SetBit(bitPosition);
			}

			sut.Flush();
		}

		// and after reopening
		using (var sut = OpenSut(10_000)) {
			for (var bitPosition = 0; bitPosition < 10_000 * 8; bitPosition++) {
				Assert.True(sut.DataAccessor.IsBitSet(bitPosition));
			}
		}
	}

	public class MemoryMappedFilePersistenceTests : PersistenceStrategyTests {
		protected override IPersistenceStrategy GenSut(
			long size, bool create, string fileName) {

			return new MemoryMappedFilePersistence(
				size, Fixture.GetFilePathFor(fileName), create);
		}
	}

	public class FileStreamFilePersistenceTests : PersistenceStrategyTests {
		protected override FileStreamPersistence GenSut(
			long size, bool create, string fileName) {

			return new FileStreamPersistence(
				size, Fixture.GetFilePathFor(fileName), create);
		}

		[Theory]
		[InlineData(10_000, 96)]
		[InlineData(256_000_000, 96)]
		[InlineData(4_000_000_000, 1120, Skip = "big")]
		public void CalculatesIntendedFlushSize(long size, long expectedFlushBatchSize) {
			var sut = GenSut(size, create: true, "thefilter");
			sut.Init();
			Assert.Equal(128, sut.FlushBatchDelay);
			Assert.Equal(expectedFlushBatchSize, sut.FlushBatchSize);
		}
	}
}
