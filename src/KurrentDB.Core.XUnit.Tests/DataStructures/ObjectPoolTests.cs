// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.DataStructures;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.DataStructures;

public class ObjectPoolTests {

	[Fact]
	public void creates_new_object_on_each_request() {
		int id = 0;
		using var pool = new ObjectPool<int>(
			objectPoolName: "test",
			initialCount: 0,
			maxCount: 3,
			factory: () => ++id
		);

		Assert.Equal(1, pool.Get());
		Assert.Equal(2, pool.Get());
		Assert.Equal(3, pool.Get());
	}

	[Fact]
	public void reuses_returned_objects() {
		int id = 0;
		using var pool = new ObjectPool<int>(
			objectPoolName: "test",
			initialCount: 0,
			maxCount: 3,
			factory: () => ++id
		);

		Assert.Equal(1, pool.Get());
		pool.Return(1);
		Assert.Equal(1, pool.Get());

		Assert.Equal(2, pool.Get());
		pool.Return(1);
		Assert.Equal(1, pool.Get());

		Assert.Equal(2, id);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void does_not_return_more_objects_than_max_count(bool markDisposingBeforeReturn) {
		int disposedObjCounter = 0;
		int disposedPoolCounter = 0;

		{
			using var pool = new ObjectPool<int>(
				objectPoolName: "test",
				initialCount: 0,
				maxCount: 3,
				factory: () => 0,
				dispose: _ => disposedObjCounter++,
				onPoolDisposed: _ => disposedPoolCounter++
			);

			var first = pool.Get();
			var second = pool.Get();
			var third = pool.Get();

			Assert.Throws<ObjectPoolMaxLimitReachedException>(() => pool.Get());

			if (markDisposingBeforeReturn) {
				pool.MarkForDisposal();
			}

			pool.Return(first);
			pool.Return(second);
			pool.Return(third);

			if (!markDisposingBeforeReturn) {
				pool.MarkForDisposal();
			}
		}

		Assert.Equal(3, disposedObjCounter);
		Assert.Equal(1, disposedPoolCounter);
	}
}
